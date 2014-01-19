﻿//
// NullProtocolLogger.cs
//
// Author: Jeffrey Stedfast <jeff@xamarin.com>
//
// Copyright (c) 2014 Jeffrey Stedfast
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//

using System;

namespace MailKit {
	public class NullProtocolLogger : IProtocolLogger
	{
		#region IProtocolLogger implementation

		/// <summary>
		/// Logs a connection to the specified URI.
		/// </summary>
		/// <param name="uri">The URI.</param>
		public void LogConnect (Uri uri)
		{
		}

		/// <summary>
		/// Logs a sequence of bytes sent by the client.
		/// </summary>
		/// <param name='buffer'>The buffer to log.</param>
		/// <param name='offset'>The offset of the first byte to log.</param>
		/// <param name='count'>The number of bytes to log.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="buffer"/> is <c>null</c>.
		public void LogClient (byte[] buffer, int offset, int count)
		{
		}

		/// <summary>
		/// Logs a sequence of bytes sent by the server.
		/// </summary>
		/// <param name='buffer'>The buffer to log.</param>
		/// <param name='offset'>The offset of the first byte to log.</param>
		/// <param name='count'>The number of bytes to log.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="buffer"/> is <c>null</c>.
		public void LogServer (byte[] buffer, int offset, int count)
		{
		}

		#endregion

		#region IDisposable implementation

		/// <summary>
		/// Releases all resource used by the <see cref="MailKit.NullProtocolLogger"/> object.
		/// </summary>
		/// <remarks>Call <see cref="Dispose"/> when you are finished using the <see cref="MailKit.NullProtocolLogger"/>. The
		/// <see cref="Dispose"/> method leaves the <see cref="MailKit.NullProtocolLogger"/> in an unusable state. After
		/// calling <see cref="Dispose"/>, you must release all references to the <see cref="MailKit.NullProtocolLogger"/> so
		/// the garbage collector can reclaim the memory that the <see cref="MailKit.NullProtocolLogger"/> was occupying.</remarks>
		public void Dispose ()
		{
		}

		#endregion
	}
}
