// 
// Gendarme.Rules.Exceptions.UseObjectDisposedExceptionRule
//
// Authors:
//	Jesse Jones <jesjones@mindspring.com>
//
// Copyright (C) 2009 Jesse Jones
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

using System;

using Mono.Cecil;
using Mono.Cecil.Cil;

using Gendarme.Framework;
using Gendarme.Framework.Engines;
using Gendarme.Framework.Helpers;
using Gendarme.Framework.Rocks;

namespace Gendarme.Rules.Exceptions {

	/// <summary>
	/// It's usually a very bad idea to attempt to use an object after it has been
	/// disposed. Doing so may lead to crashes in native code or any number of
	/// other problems. In order to prevent this, and to report the problem in
	/// a clear way, classes should throw System.ObjectDisposedException from
	/// public methods if the object has been disposed.
	///
	/// Note that there are some methods which should not throw ObjectDisposedException. 
	/// This includes constructors, finalizers, Equals, GetHashCode, ToString, and Dispose.
	/// </summary>
	/// <example>
	/// Bad example:
	/// <code>
	/// internal sealed class WriteStuff : IDisposable
	/// {
	/// 	public WriteStuff (TextWriter writer)
	/// 	{
	/// 		this.writer = writer;
	/// 	}
	/// 	
	/// 	// Objects are generally not in a useable state after being disposed so
	/// 	// their public methods should throw ObjectDisposedException.
	/// 	public void Write (string message)
	/// 	{
	/// 		writer.Write (message);
	/// 	}
	/// 	
	/// 	public void Dispose ()
	/// 	{
	/// 		if (!disposed) {
	/// 			writer.Dispose ();
	/// 			disposed = true;
	/// 		}
	/// 	}
	/// 	
	/// 	private bool disposed;
	/// 	private TextWriter writer;
	/// }
	/// </code>
	/// </example>
	/// <example>
	/// Good example:
	/// <code>
	/// internal sealed class WriteStuff : IDisposable
	/// {
	/// 	public WriteStuff (TextWriter writer)
	/// 	{
	/// 		this.writer = writer;
	/// 	}
	/// 	
	/// 	// In general all public methods should throw ObjectDisposedException
	/// 	// if Dispose has been called.
	/// 	public void Write (string message)
	/// 	{
	/// 		if (disposed) {
	/// 			throw new ObjectDisposedException (GetType ().Name);
	/// 		}
	/// 		
	/// 		writer.Write (message);
	/// 	}
	/// 	
	/// 	public void Dispose ()
	/// 	{
	/// 		if (!disposed) {
	/// 			writer.Dispose ();
	/// 			disposed = true;
	/// 		}
	/// 	}
	/// 	
	/// 	private bool disposed;
	/// 	private TextWriter writer;
	/// }
	/// </code>
	/// </example>
	/// <remarks>This rule is available since Gendarme 2.6</remarks>

	[Problem ("A method of an IDisposable type does not throw System.ObjectDisposedException.")]
	[Solution ("Throw ObjectDisposedException if the object has been disposed.")]
	public sealed class UseObjectDisposedExceptionRule : Rule, IMethodRule {
		
		public RuleResult CheckMethod (MethodDefinition method)
		{
			if (!method.HasBody)
				return RuleResult.DoesNotApply;
			
			// Don't want to count the code generated by yield statements.
			if (method.IsGeneratedCode ())
				return RuleResult.DoesNotApply;
			
			if (Preflight (method)) {
				Log.WriteLine (this);
				Log.WriteLine (this, "-----------------------------------------");
				Log.WriteLine (this, method);
				
				has_this_call = false;
				has_this_field = false;
				creates_exception = false;
				has_dispose_check = false;
				
				CheckBody (method);
				
				if ((has_this_call || has_this_field) && !creates_exception) {
					if (!has_dispose_check) {
						Runner.Report (method, Severity.Medium, Confidence.High);
					}
				}
			}
			
			return Runner.CurrentRuleResult;
		}
		
		private void CheckBody (MethodDefinition method)
		{
			string fullname = method.DeclaringType.FullName;
			foreach (Instruction ins in method.Body.Instructions) {
				switch (ins.OpCode.Code) {
				case Code.Call:
				case Code.Callvirt:
					MethodReference target = (MethodReference) ins.Operand;
					if (!has_this_call) {
						MethodDefinition callee = target.Resolve ();
						if (callee != null) {
							if (!callee.IsPublic && !callee.IsStatic) {
								if (callee.DeclaringType.FullName == fullname) {
									Instruction instance = ins.TraceBack (method);
									if (instance != null && instance.OpCode.Code == Code.Ldarg_0) {
										Log.WriteLine (this, "found non-public this call at {0:X4}", ins.Offset);
										has_this_call = true;
									}
								}
							}
						}
					}
					
					// Special case for helper methods like CheckIfClosedThrowDisposed or
					// CheckObjectDisposedException.
					if (!has_dispose_check) {
						string tname = target.Name;
						if (tname.Contains ("Check") && tname.Contains ("Dispose")) {
							Log.WriteLine (this, "found dispose check at {0:X4}", ins.Offset);
							has_dispose_check = true;
						}
					}
					break;
				
				case Code.Ldfld:
				case Code.Stfld:
				case Code.Ldflda:
					if (!has_this_field) {
						FieldReference field = (FieldReference) ins.Operand;
						if (field.DeclaringType.FullName == fullname) {
							Instruction instance = ins.TraceBack (method);
							if (instance != null && instance.OpCode.Code == Code.Ldarg_0) {
								Log.WriteLine (this, "found field access at {0:X4}", ins.Offset);
								has_this_field = true;
							}
						}
					}
					break;
				
				case Code.Newobj:
					if (!creates_exception) {
						MethodReference ctor = (MethodReference) ins.Operand;
						if (ctor.DeclaringType.FullName == "System.ObjectDisposedException") {
							Log.WriteLine (this, "creates exception at {0:X4}", ins.Offset);
							creates_exception = true;
						}
					}
					break;
				}
			}
		}
		
		// Skip methods which we don't want to examine in detail. Note that this
		// will skip methods which don't call a method or touch a field. This is done 
		// to eliminate annoying cases like methods that do nothing but throw 
		// something like a not implemented exception.
		static bool Preflight (MethodDefinition method)
		{
			bool needs = false;
			
			if (method.IsPublic) {
				if (OpCodeEngine.GetBitmask (method).Intersect (CallsAndFields)) {
					if (method.DeclaringType.Implements ("System.IDisposable")) {
						if (AllowedToThrow (method)) {
							needs = true;
						}
					}
				}
			}
			
			return needs;
		}
		
		// Note that auto-setters are considered to be generated code so we won't
		// make it this far for them.
		static bool AllowedToThrow (MethodDefinition method)
		{
			if (method.IsConstructor)
				return false;
				
			if (MethodSignatures.Finalize.Matches (method))
				return false;
				
			if (method.IsGetter)
				return false;
			
			if (method.IsAddOn || method.IsRemoveOn || method.IsFire)
				return false;
			
			if (Equals1.Matches (method))
				return false;
				
			if (MethodSignatures.GetHashCode.Matches (method))
				return false;
				
			if (MethodSignatures.ToString.Matches (method))
				return false;
				
			if (Close.Matches (method))
				return false;
				
			if (method.Name == "Dispose")
				return false;
			
			return true;
		}
		
#if false
		private void Bitmask ()
		{
			OpCodeBitmask mask = new OpCodeBitmask ();
			mask.Set (Code.Call);
			mask.Set (Code.Callvirt);
			mask.Set (Code.Ldfld);
			mask.Set (Code.Ldflda);
			mask.Set (Code.Stfld);
			mask.Set (Code.Newobj);
			Console.WriteLine (mask);
		}
#endif
		
		private static readonly OpCodeBitmask CallsAndFields = new OpCodeBitmask (0x8000000000, 0x704400000000000, 0x0, 0x0);
		private static readonly MethodSignature Equals1 = new MethodSignature ("Equals", "System.Boolean", new string [1]);
		private static readonly MethodSignature Close = new MethodSignature ("Close", "System.Void", new string [0]);
		
		private bool has_this_call;
		private bool has_this_field;
		private bool creates_exception;
		private bool has_dispose_check;
	}
}
