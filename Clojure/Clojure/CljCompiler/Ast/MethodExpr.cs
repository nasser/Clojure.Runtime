/**
 *   Copyright (c) Rich Hickey. All rights reserved.
 *   The use and distribution terms for this software are covered by the
 *   Eclipse Public License 1.0 (http://opensource.org/licenses/eclipse-1.0.php)
 *   which can be found in the file epl-v10.html at the root of this distribution.
 *   By using this software in any fashion, you are agreeing to be bound by
 * 	 the terms of this license.
 *   You must not remove this notice, or any other, from this software.
 **/

/**
 *   Author: David Miller
 **/

using System;
using System.Linq;
using System.Collections.Generic;

#if CLR2
using Microsoft.Scripting.Ast;
#else
using System.Linq.Expressions;
#endif
using System.Dynamic;
using System.Reflection;
using clojure.lang.Runtime.Binding;
using clojure.lang.Runtime;
using System.Reflection.Emit;

namespace clojure.lang.CljCompiler.Ast
{
    abstract class MethodExpr : HostExpr
    {
        #region Data

        protected readonly string _methodName;
        protected readonly List<HostArg> _args;
        protected readonly List<Type> _typeArgs;
        protected MethodInfo _method;
        protected readonly string _source;
        protected readonly IPersistentMap _spanMap;
        protected readonly Symbol _tag;

        #endregion

        #region C-tors

        protected MethodExpr(string source, IPersistentMap spanMap, Symbol tag, string methodName, List<Type> typeArgs, List<HostArg> args)
        {
            _source = source;
            _spanMap = spanMap;
            _methodName = methodName;
            _typeArgs = typeArgs;
            _args = args;
            _tag = tag;
        }

        #endregion

        #region Code generation

        protected abstract bool IsStaticCall { get; }

        public override bool CanEmitPrimitive
        {
            get { return _method != null && Util.IsPrimitive(_method.ReturnType); }
        }

        public override void Emit(RHC rhc, ObjExpr objx, CljILGen ilg)
        {
            GenContext.EmitDebugInfo(ilg, _spanMap);

            Type retType;

            if (_method != null)
            {
                EmitForMethod(objx, ilg);
                retType = _method.ReturnType;
            }
            else
            {
                EmitDynamicCall(objx, ilg);
                retType = typeof(object);
            }
            HostExpr.EmitBoxReturn(objx, ilg, retType);

            if (rhc == RHC.Statement)
                ilg.Emit(OpCodes.Pop);
        }

        public override void EmitUnboxed(RHC rhc, ObjExpr objx, CljILGen ilg)
        {
            GenContext.EmitDebugInfo(ilg, _spanMap);

            if (_method != null)
            {
                EmitForMethod(objx, ilg);
            }
            else
            {
                throw new InvalidOperationException("Unboxed emit of unknown member.");
            }

            if (rhc == RHC.Statement)
               ilg.Emit(OpCodes.Pop);
        }

        private void EmitForMethod(ObjExpr objx, CljILGen ilg)
        {
            if ( _method.IsGenericMethodDefinition )
            {
                EmitDynamicCall(objx, ilg);
                return;
            }

            if (!IsStaticCall)
            {
                EmitTargetExpression(objx, ilg);
                // EmitPrepForCall(ilg,typeof(object),_method.DeclaringType);
            }

            EmitTypedArgs(objx, ilg, _method.GetParameters(), _args);
            if (IsStaticCall)
            {
                if (Intrinsics.HasOp(_method))
                    Intrinsics.EmitOp(_method,ilg);
                else
                    ilg.Emit(OpCodes.Call, _method);
            }
            else
                ilg.Emit(OpCodes.Callvirt, _method); 
        }

		public void EmitDynamicCall(ObjExpr objx, CljILGen ilg)
		{
			ilg.EmitFieldGet (objx.InlineCacheFields [_methodName]);
			EmitTargetExpression (objx, ilg);
			// ilg.EmitArray<object> (_args.Map(a => a.ArgExpr.Emit)); // what type should the array be? -nasser 
			ilg.EmitArray (typeof(object), _args.Count, i => _args [i].ArgExpr.Emit (RHC.Expression, objx, ilg));
			ilg.EmitCall (Compiler.Method_InlineCache_Invoke);
		}

        protected abstract void EmitTargetExpression(ObjExpr objx, CljILGen ilg);
        protected abstract Type GetTargetType();

        public static void EmitByRefArg(HostArg ha, ObjExpr objx, CljILGen ilg)
        {
            if (ha.LocalBinding.IsArg)
                ilg.Emit(OpCodes.Ldarga, ha.LocalBinding.Index);
            else if (ha.LocalBinding.IsThis)
                ilg.Emit(OpCodes.Ldarga, 0);
            else
                ilg.Emit(OpCodes.Ldloca, ha.LocalBinding.LocalVar);
        }
			
        
        internal static void EmitArgsAsArray(IPersistentVector args, ObjExpr objx, CljILGen ilg)
        {
            ilg.EmitInt(args.count());
            ilg.Emit(OpCodes.Newarr, typeof(Object));

            for (int i = 0; i < args.count(); i++)
            {
                ilg.Emit(OpCodes.Dup);
                ilg.EmitInt(i);
                ((Expr)args.nth(i)).Emit(RHC.Expression, objx, ilg);
                ilg.Emit(OpCodes.Stelem_Ref);
            }
        }

        public static void EmitTypedArgs(ObjExpr objx, CljILGen ilg, ParameterInfo[] parms, List<HostArg> args)
        {
            for (int i = 0; i < parms.Length; i++)
            {
                HostArg ha = args[i];
                ParameterInfo pi = parms[i];
                bool argIsByRef = ha.ParamType == HostArg.ParameterType.ByRef;
                bool paramIsByRef = pi.ParameterType.IsByRef;

                if (!paramIsByRef)
                    EmitTypedArg(objx, ilg, pi.ParameterType, ha.ArgExpr);
                else // paramIsByRef
                {
                    if (argIsByRef)
                    {
                        EmitByRefArg(ha, objx, ilg);
                    }
                    else
                    {
                        EmitTypedArg(objx, ilg, parms[i].ParameterType, args[i].ArgExpr);
                        LocalBuilder loc = ilg.DeclareLocal(pi.ParameterType);
                        loc.SetLocalSymInfo("_byRef_temp" + i);
                        ilg.Emit(OpCodes.Stloc, loc);
                        ilg.Emit(OpCodes.Ldloca, loc);
                    }
                }
            }
        }

        public static void EmitTypedArgs(ObjExpr objx, CljILGen ilg, ParameterInfo[] parms, IPersistentVector args)
        {
            for (int i = 0; i < parms.Length; i++)
                EmitTypedArg(objx, ilg, parms[i].ParameterType, (Expr)args.nth(i));
        }

        public static void EmitTypedArg(ObjExpr objx, CljILGen ilg, Type paramType, Expr arg)
        {
            Type primt = Compiler.MaybePrimitiveType(arg);
            MaybePrimitiveExpr mpe = arg as MaybePrimitiveExpr;

            if (primt == paramType)
            {
                mpe.EmitUnboxed(RHC.Expression, objx, ilg);
            }
            else if (primt == typeof(int) && paramType == typeof(long))
            {
                mpe.EmitUnboxed(RHC.Expression, objx, ilg);
                ilg.Emit(OpCodes.Conv_I8);
             }
            else if (primt == typeof(long) && paramType == typeof(int))
            {
                mpe.EmitUnboxed(RHC.Expression, objx, ilg);
                if (RT.booleanCast(RT.UncheckedMathVar.deref()))
                    ilg.Emit(OpCodes.Call,Compiler.Method_RT_uncheckedIntCast_long);
                else
                    ilg.Emit(OpCodes.Call,Compiler.Method_RT_intCast_long);
            }
            else if (primt == typeof(float) && paramType == typeof(double))
            {
                mpe.EmitUnboxed(RHC.Expression, objx, ilg);
                ilg.Emit(OpCodes.Conv_R8);
            }
            else if (primt == typeof(double) && paramType == typeof(float))
            {
                mpe.EmitUnboxed(RHC.Expression, objx, ilg);
                ilg.Emit(OpCodes.Conv_R4);
            }
            else
            {
                arg.Emit(RHC.Expression, objx, ilg);
                HostExpr.EmitUnboxArg(objx, ilg, paramType);
            }
        }

        #endregion
    }
}
