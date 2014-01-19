﻿//
// ImapCommand.cs
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
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;

using MimeKit;
using MimeKit.IO;
using MimeKit.IO.Filters;

namespace MailKit.Net.Imap {
	/// <summary>
	/// IMAP continuation handler.
	/// </summary>
	/// <remarks>
	/// All exceptions thrown by the handler are considered fatal and will
	/// force-disconnect the connection. If a non-fatal error occurs, set
	/// it on the <see cref="ImapCommand.Exception"/> property.
	/// </remarks>
	delegate void ImapContinuationHandler (ImapEngine engine, ImapCommand ic, string text);

	/// <summary>
	/// IMAP untagged response handler.
	/// </summary>
	/// <remarks>
	/// <para>Most IMAP commands return their results in untagged responses.</para>
	/// </remarks>
	delegate void ImapUntaggedHandler (ImapEngine engine, ImapCommand ic, ImapToken token);

	delegate void ImapCommandResetHandler (ImapCommand ic);

	/// <summary>
	/// IMAP command status.
	/// </summary>
	enum ImapCommandStatus {
		Queued,
		Active,
		Complete,
		Error
	}

	enum ImapCommandResult {
		None,
		Ok,
		No,
		Bad
	}

	enum ImapLiteralType {
		String,
		Stream,
		MimeMessage
	}

	enum ImapStringType {
		Atom,
		QString,
		Literal
	}

	/// <summary>
	/// An IMAP literal object.
	/// </summary>
	class ImapLiteral
	{
		public readonly ImapLiteralType Type;
		public readonly object Literal;

		public ImapLiteral (object literal)
		{
			if (literal is MimeMessage) {
				Type = ImapLiteralType.MimeMessage;
			} else if (literal is Stream) {
				Type = ImapLiteralType.Stream;
			} else if (literal is string) {
				literal = Encoding.UTF8.GetBytes ((string) literal);
				Type = ImapLiteralType.String;
			} else if (literal is byte[]) {
				Type = ImapLiteralType.String;
			} else {
				throw new ArgumentException ("Unknown literal type");
			}

			Literal = literal;
		}

		/// <summary>
		/// Gets the length of the literal, in bytes.
		/// </summary>
		/// <value>The length.</value>
		public long Length {
			get {
				if (Type == ImapLiteralType.String)
					return (long) ((byte[]) Literal).Length;

				using (var measure = new MeasuringStream ()) {
					switch (Type) {
					case ImapLiteralType.Stream:
						var stream = (Stream) Literal;
						stream.CopyTo (measure, 4096);
						stream.Position = 0;
						break;
					case ImapLiteralType.MimeMessage:
						var options = FormatOptions.Default.Clone ();
						options.NewLineFormat = NewLineFormat.Dos;

						((MimeMessage) Literal).WriteTo (options, measure);
						break;
					}

					return measure.Length;
				}
			}
		}

		public void WriteTo (Stream stream, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested ();

			if (Type == ImapLiteralType.String) {
				var bytes = (byte[]) Literal;
				stream.Write (bytes, 0, bytes.Length);
				stream.Flush ();
				return;
			}

			if (Type == ImapLiteralType.MimeMessage) {
				var options = FormatOptions.Default.Clone ();
				options.NewLineFormat = NewLineFormat.Dos;

				var message = (MimeMessage) Literal;

				message.WriteTo (options, stream, cancellationToken);
				stream.Flush ();
				return;
			}

			var literal = (Stream) Literal;
			var buf = new byte[4096];
			int nread = 0;

			while ((nread = literal.Read (buf, 0, buf.Length)) > 0) {
				cancellationToken.ThrowIfCancellationRequested ();
				stream.Write (buf, 0, nread);
			}

			stream.Flush ();
		}
	}

	class ImapCommandPart
	{
		public readonly byte[] Command;
		public readonly ImapLiteral Literal;

		public ImapCommandPart (byte[] command, ImapLiteral literal)
		{
			Command = command;
			Literal = literal;
		}
	}

	class ImapCommand
	{
		public Dictionary<string, ImapUntaggedHandler> UntaggedHandlers { get; private set; }
		public ImapContinuationHandler ContinuationHandler { get; set; }
		public CancellationToken CancellationToken { get; private set; }
		public ImapCommandStatus Status { get; internal set; }
		public ImapCommandResult Result { get; internal set; }
		public ImapException Exception { get; internal set; }
		public readonly List<ImapResponseCode> RespCodes;
		public ImapFolder Folder { get; private set; }
		public string Tag { get; private set; }
		public int Id { get; internal set; }

		readonly List<ImapCommandPart> parts = new List<ImapCommandPart> ();
		readonly ImapEngine Engine;
		int current = 0;

		public ImapCommand (ImapEngine engine, CancellationToken cancellationToken, ImapFolder folder, string format, params object[] args)
		{
			UntaggedHandlers = new Dictionary<string, ImapUntaggedHandler> ();
			RespCodes = new List<ImapResponseCode> ();
			Status = ImapCommandStatus.Queued;
			Result = ImapCommandResult.None;
			CancellationToken = cancellationToken;
			Engine = engine;
			Folder = folder;

			using (var builder = new MemoryStream ()) {
				int argc = 0;
				byte[] buf;
				string str;
				char c;

				for (int i = 0; i < format.Length; i++) {
					if (format[i] == '%') {
						switch (format[++i]) {
						case '%': // a literal %
							builder.WriteByte ((byte) '%');
							break;
						case 'c': // a character
							c = (char) args[argc++];
							builder.WriteByte ((byte) c);
							break;
						case 'd': // an integer
							str = ((int) args[argc++]).ToString ();
							buf = Encoding.ASCII.GetBytes (str);
							builder.Write (buf, 0, buf.Length);
							break;
						case 'u': // an unsigned integer
							str = ((uint) args[argc++]).ToString ();
							buf = Encoding.ASCII.GetBytes (str);
							builder.Write (buf, 0, buf.Length);
							break;
						case 'F': // an ImapFolder
							var utf7 = ((ImapFolder) args[argc++]).EncodedName;
							AppendString (builder, utf7);
							break;
						case 'L':
							var literal = new ImapLiteral (args[argc++]);
							var length = literal.Length;

							buf = Encoding.ASCII.GetBytes (length.ToString ());

							// FIXME: support LITERAL+?
							builder.WriteByte ((byte) '{');
							builder.Write (buf, 0, buf.Length);
							builder.WriteByte ((byte) '}');
							builder.WriteByte ((byte) '\r');
							builder.WriteByte ((byte) '\n');

							parts.Add (new ImapCommandPart (builder.ToArray (), literal));
							builder.SetLength (0);
							break;
						case 'S': // a string which may need to be quoted or made into a literal
							AppendString (builder, (string) args[argc++]);
							break;
						case 's': // a safe atom string
							buf = Encoding.ASCII.GetBytes ((string) args[argc++]);
							builder.Write (buf, 0, buf.Length);
							break;
						default:
							throw new FormatException ();
						}
					} else {
						builder.WriteByte ((byte) format[i]);
					}
				}

				parts.Add (new ImapCommandPart (builder.ToArray (), null));
			}
		}

		static bool IsAtom (char c)
		{
			return c < 128 && !char.IsControl (c) && !char.IsWhiteSpace (c) && "()[}*%\\\"\n".IndexOf (c) == -1;
		}

		static bool IsQuotedSafe (char c)
		{
			return c < 128 && !char.IsControl (c) && c != '\\' && c != '"';
		}

		static ImapStringType GetStringType (string value)
		{
			var type = ImapStringType.Atom;

			for (int i = 0; i < value.Length; i++) {
				if (!IsAtom (value[i])) {
					if (!IsQuotedSafe (value[i]))
						return ImapStringType.Literal;

					type = ImapStringType.QString;
				}
			}

			return type;
		}

		void AppendString (MemoryStream builder, string value)
		{
			byte[] buf;

			switch (GetStringType (value)) {
			case ImapStringType.Literal:
				var literal = Encoding.UTF8.GetBytes (value);
				var length = literal.Length.ToString ();
				buf = Encoding.ASCII.GetBytes (length);

				builder.WriteByte ((byte) '{');
				builder.Write (buf, 0, buf.Length);
				if ((Engine.Capabilities & ImapCapabilities.LiteralPlus) != 0)
					builder.WriteByte ((byte) '+');
				builder.WriteByte ((byte) '}');
				builder.WriteByte ((byte) '\r');
				builder.WriteByte ((byte) '\n');

				if ((Engine.Capabilities & ImapCapabilities.LiteralPlus) != 0) {
					builder.Write (literal, 0, literal.Length);
				} else {
					parts.Add (new ImapCommandPart (builder.ToArray (), new ImapLiteral (literal)));
					builder.SetLength (0);
				}
				break;
			case ImapStringType.QString:
				buf = Encoding.UTF8.GetBytes (value);
				builder.WriteByte ((byte) '"');
				builder.Write (buf, 0, buf.Length);
				builder.WriteByte ((byte) '"');
				break;
			case ImapStringType.Atom:
				buf = Encoding.UTF8.GetBytes (value);
				builder.Write (buf, 0, buf.Length);
				break;
			}
		}

		/// <summary>
		/// Sends the next part of the command to the server.
		/// </summary>
		public bool Step ()
		{
			var result = ImapCommandResult.None;

			CancellationToken.ThrowIfCancellationRequested ();

			if (current == 0) {
				Tag = string.Format ("{0}{1:D8}", Engine.TagPrefix, Engine.Tag++);
				
				var buf = Encoding.ASCII.GetBytes (Tag + " ");
				Engine.Stream.Write (buf, 0, buf.Length);
			}

			Engine.Stream.Write (parts[current].Command, 0, parts[current].Command.Length);
			Engine.Stream.Flush ();

			// now we need to read the response...
			do {
				var token = Engine.Stream.ReadToken (CancellationToken);

				if (token.Type == ImapTokenType.Plus) {
					// we've gotten a continuation response from the server
					var text = Engine.ReadLine (CancellationToken).Trim ();

					// if we've got a Literal pending, the '+' means we can send it now...
					if (parts[current].Literal != null) {
						parts[current].Literal.WriteTo (Engine.Stream, CancellationToken);
						break;
					}

					Debug.Assert (ContinuationHandler != null, "The ImapCommand's ContinuationHandler is null");

					ContinuationHandler (Engine, this, text);
				} else if (token.Type == ImapTokenType.Asterisk) {
					// we got an untagged response, let the engine handle this...
					Engine.ProcessUntaggedResponse (CancellationToken);
				} else if (token.Type == ImapTokenType.Atom && (string) token.Value == Tag) {
					// the next token should be "OK", "NO", or "BAD"
					token = Engine.Stream.ReadToken (CancellationToken);

					if (token.Type == ImapTokenType.Atom) {
						string atom = (string) token.Value;

						switch (atom) {
						case "BAD": result = ImapCommandResult.Bad; break;
						case "OK": result = ImapCommandResult.Ok; break;
						case "NO": result = ImapCommandResult.No; break;
						}

						if (result == ImapCommandResult.None)
							throw ImapEngine.UnexpectedToken (token, false);

						token = Engine.Stream.ReadToken (CancellationToken);
						if (token.Type == ImapTokenType.OpenBracket) {
							var code = Engine.ParseResponseCode (CancellationToken);
							// FIXME: handle the response code?
						} else if (token.Type != ImapTokenType.Eoln) {
							// consume the rest of the line...
							Engine.ReadLine (CancellationToken);
						}
					} else {
						// looks like we didn't get an "OK", "NO", or "BAD"...
						throw ImapEngine.UnexpectedToken (token, false);
					}
				} else {
					// no clue what we got...
					throw ImapEngine.UnexpectedToken (token, false);
				}
			} while (true);

			// the status should always be Active at this point, but just to be sure...
			if (Status == ImapCommandStatus.Active) {
				current++;

				if (current == parts.Count || result != ImapCommandResult.None) {
					Status = ImapCommandStatus.Complete;
					Result = Result;
					return false;
				}
			}

			return true;
		}
	}
}
