﻿using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Voron.Trees;

namespace Voron.Impl.Paging
{
	public unsafe class PureMemoryPager : AbstractPager
	{
		private ImmutableList<Buffer> _buffers = ImmutableList<Buffer>.Empty;

		public class Buffer
		{
			public byte* Base;
			public long Size;
			public IntPtr Handle;
		}

		public PureMemoryPager(byte[] data)
		{
			var ptr = Marshal.AllocHGlobal(data.Length);
			var buffer = new Buffer
			{
				Handle = ptr,
				Base = (byte*)ptr.ToPointer(),
				Size = data.Length
			};
			_buffers = _buffers.Add(buffer);
			NumberOfAllocatedPages = data.Length / PageSize;
			PagerState.Release();
			PagerState = new PagerState();
			PagerState.AddRef();
			fixed (byte* origin = data)
			{
				NativeMethods.memcpy(buffer.Base, origin, data.Length);
			}
		}

		public PureMemoryPager()
		{
			var ptr = Marshal.AllocHGlobal(MinIncreaseSize);
			var buffer = new Buffer
			{
				Handle = ptr,
				Base = (byte*)ptr.ToPointer(),
				Size = MinIncreaseSize
			};
			_buffers.Add(buffer);
			NumberOfAllocatedPages = 0;
			PagerState.Release();
			PagerState = new PagerState();
			PagerState.AddRef();
		}

	    public override void Write(Page page, long? pageNumber)
	    {
			var toWrite = page.IsOverflow ? GetNumberOfOverflowPages(page.OverflowSize): 1;
	        var requestedPageNumber = pageNumber ?? page.PageNumber;
            
            WriteDirect(page, requestedPageNumber, toWrite);
	    }

	    public override string ToString()
	    {
	        return "memory";
	    }

	    public override void WriteDirect(Page start, long pagePosition, int pagesToWrite)
	    {
            EnsureContinuous(null, pagePosition, pagesToWrite);
            NativeMethods.memcpy(AcquirePagePointer(pagePosition), start.Base, pagesToWrite * PageSize);
	    }

	    public override void Dispose()
		{
			base.Dispose();
			PagerState.Release();
		    foreach (var buffer in _buffers)
		    {
			    if (buffer.Handle == IntPtr.Zero)
				    continue;
				Marshal.FreeHGlobal(new IntPtr(buffer.Base));
			    buffer.Handle = IntPtr.Zero;
		    }
		    _buffers = _buffers.Clear();
		}

		public override void Sync()
		{
			// nothing to do here
		}

		public override byte* AcquirePagePointer(long pageNumber)
		{
			long size = pageNumber*PageSize;

			foreach (var buffer in _buffers)
			{
				if (buffer.Size > size)
				{
					return buffer.Base + (size);
				}
				size -= buffer.Size;
			}

			throw new ArgumentException("Page number beyond the end of allocated pages");
		}

		public override void AllocateMorePages(Transaction tx, long newLength)
		{
			var oldSize = NumberOfAllocatedPages * PageSize;
			if (newLength < oldSize)
				throw new ArgumentException("Cannot set the legnth to less than the current length");
			if (newLength == oldSize)
		        return; // nothing to do

			var increaseSize = (newLength - oldSize);
			NumberOfAllocatedPages += increaseSize / PageSize;
			var newPtr = Marshal.AllocHGlobal(new IntPtr(increaseSize));

			var buffer = new Buffer
			{
				Handle = newPtr,
				Base = (byte*) newPtr.ToPointer(),
				Size = increaseSize
			};

			_buffers = _buffers.Add(buffer);

			var newPager = new PagerState { };
			newPager.AddRef(); // one for the pager

			if (tx != null) // we only pass null during startup, and we don't need it there
			{
				newPager.AddRef(); // one for the current transaction
				tx.AddPagerState(newPager);
			}
			PagerState.Release();
			PagerState = newPager;
		}
	}
}