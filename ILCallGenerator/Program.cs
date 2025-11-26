using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using GenericParameterAttributes = Mono.Cecil.GenericParameterAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using MethodImplAttributes = Mono.Cecil.MethodImplAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace ConsoleApplication2
{
    internal class Program
    {
        public static T Get<T>()
            where T : unmanaged
        {
            return default;
        }

        public struct ModDefinition
        {
            public string name;
            public MethodCallingConvention callingConvention;
            public bool hasThis;
            public bool isUnity;
            public bool il2cpp;
            public bool IsManaged => callingConvention == MethodCallingConvention.Default;
        }

        private static Type[] funcPtrType = new Type[] { typeof(IntPtr) };
        private static ModuleDefinition module;
        private static AssemblyDefinition dynamicAssembly;
        private static MethodReference invalidOperationExceptionConstructor;

        //private static TypeBuilder refReturnType;

        private static Type[] returnTypes = new Type[]
        {
            typeof(byte),
            typeof(ushort),
            typeof(short),
            typeof(uint),
            typeof(int),
            typeof(long),
            typeof(ulong),
            typeof(string),
            typeof(object),
            typeof(IntPtr),
            typeof(float),
            typeof(double),
        };

        public static ModStruct AddModType(ModDefinition definition)
        {
            var type = module.DefineType("", $"Func{definition.name}", TypeAttributes.Public | TypeAttributes.SequentialLayout);
            type.BaseType = module.ImportReference(typeof(ValueType));
            module.Types.Add(type);

            FieldDefinition funcPtrField = null;
            if (definition.isUnity && !definition.il2cpp)
                funcPtrField = type.DefineField("methodPtr", typeof(ulong), FieldAttributes.Public);
            else
                funcPtrField = type.DefineField("methodPtr", typeof(IntPtr), FieldAttributes.Public);

            const int maxNumArgs = 8;
            for (int i = 0; i <= maxNumArgs; i++)
            {
                for (int retType = 0; retType < 4; retType++)
                {
                    if (definition.hasThis)
                    {
                        AddMethod(true, false, funcPtrField, definition, type, retType, i);
                        AddMethod(true, true, funcPtrField, definition, type, retType, i);
                        //AddPointerStubMethod(definition, type, method, i);
                        //AddReferenceStubMethod(definition, type, method, i);
                    }
                    else
                    {
                        AddMethod(false, false, funcPtrField, definition, type, retType, i);
                        //AddPointerStubMethod(definition, type, method1, i);
                        //AddReferenceStubMethod(definition, type, method1, i);
                        //AddPointerStubMethod(definition, type, method2, i);
                        //AddReferenceStubMethod(definition, type, method2, i);
                    }
                }
               
                
                //for (int typeId = 0; typeId < returnTypes.Length; typeId++)
                //    AddTypeForward(returnTypes[typeId], definition, type, method, i);
            }

            for (int i = 0; i <= maxNumArgs; i++)
            {
                if (definition.hasThis)
                {
                    AddMethod(true, false, funcPtrField, definition, type, 0, i);
                    AddMethod(true, true, funcPtrField, definition, type, 0, i);
                }
                else
                {
                    AddMethod(false, false, funcPtrField, definition, type, 0, i);
                }
            }

            if (!definition.isUnity || definition.il2cpp)
            {
                var initMethod = type.DefineMethod("FromPointer", MethodAttributes.Public | MethodAttributes.Static);
                initMethod.ReturnType = type;
                initMethod.Parameters.Add(new ParameterDefinition("ptr", ParameterAttributes.None, module.TypeSystem.IntPtr));
                initMethod.ImplAttributes |= MethodImplAttributes.AggressiveInlining;

                var il = initMethod.Body.GetILProcessor();
                il.DeclareLocal(type);
                il.Emit(OpCodes.Ldloca_S, (byte)0);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Stfld, funcPtrField);
                il.Emit(OpCodes.Ldloc_S, (byte)0);
                il.Emit(OpCodes.Ret);

                var initMethod2 = type.DefineMethod("FromPointer", MethodAttributes.Public | MethodAttributes.Static);
                initMethod2.ReturnType = type;
                initMethod2.Parameters.Add(new ParameterDefinition("ptr", ParameterAttributes.None, new PointerType(module.TypeSystem.Void)));
                initMethod2.ImplAttributes |= MethodImplAttributes.AggressiveInlining;

                il = initMethod2.Body.GetILProcessor();
                il.DeclareLocal(type);
                il.Emit(OpCodes.Ldloca_S, (byte)0);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Stfld, funcPtrField);
                il.Emit(OpCodes.Ldloc_S, (byte)0);
                il.Emit(OpCodes.Ret);
            }
            else if (definition.isUnity && !definition.il2cpp)
            {
                var initMethod = type.DefineMethod("FromPointer", MethodAttributes.Public | MethodAttributes.Static);
                initMethod.ReturnType = type;
                initMethod.Parameters.Add(new ParameterDefinition("ptr", ParameterAttributes.None, module.TypeSystem.IntPtr));
                initMethod.Parameters.Add(new ParameterDefinition("x", ParameterAttributes.None, module.TypeSystem.Boolean));

                initMethod.DefineParameter(1, ParameterAttributes.None, "funcPtr");
                var p = initMethod.DefineParameter(2, ParameterAttributes.Optional | ParameterAttributes.HasDefault,
                    "isIL2CPPDirect");
                p.Constant = false;

                initMethod.ImplAttributes |= MethodImplAttributes.AggressiveInlining;
                
                var il = initMethod.Body.GetILProcessor();
                il.DeclareLocal(typeof(ulong));
                var label = il.DefineLabel();
                var skip4byteTransform = il.DefineLabel();

                il.DeclareLocal(type);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Conv_U8);
                il.Emit(OpCodes.Stloc_0);

                il.Emit(OpCodes.Sizeof, module.TypeSystem.Void.MakePointerType());
                il.Emit(OpCodes.Ldc_I4_4);
                il.Emit(OpCodes.Bne_Un_S, skip4byteTransform);

                il.Emit(OpCodes.Ldloc_0);
                il.Emit(OpCodes.Ldc_I4_M1);
                il.Emit(OpCodes.Conv_U8);
                il.Emit(OpCodes.And);

                il.Emit(OpCodes.Stloc_0);

                il.MarkLabel(skip4byteTransform);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Brtrue_S, label);

                il.Emit(OpCodes.Ldloca_S, (byte)1);
                il.Emit(OpCodes.Ldloc_0);
                il.Emit(OpCodes.Stfld, funcPtrField);
                il.Emit(OpCodes.Ldloc_S, (byte)1);
                il.Emit(OpCodes.Ret);

                il.MarkLabel(label);

                il.Emit(OpCodes.Ldloca_S, (byte)1);
                il.Emit(OpCodes.Ldloc_0);
                il.Emit(OpCodes.Ldc_I8, -9223372036854775808L);
                //il.Emit(OpCodes.Conv_U8);
                il.Emit(OpCodes.Or);
                il.Emit(OpCodes.Stfld, funcPtrField);
                il.Emit(OpCodes.Ldloc_S, (byte)1);
                il.Emit(OpCodes.Ret);
            }

            //type.CreateType();
            return new ModStruct()
            {
                type = type,
                definition = definition,
                methodPtr = funcPtrField
            };
        }

        private static unsafe MethodDefinition AddMethod(bool hasThis, bool thisByRef, FieldDefinition field, ModDefinition definition, TypeDefinition type,
            int returnType, int numArgs)
        {
            string methodName = null;
            bool hasReturn = returnType != 0;
            bool hasGenericReturn = hasReturn && returnType >= 1;

            switch (returnType)
            {
                case 0:
                    methodName = "Void"; break;
                case 1:
                    methodName = "Generic"; break;
                case 2:
                    methodName = "Ref"; break;
                case 3:
                    methodName = "Ptr"; break;
            }
            
            var callMethod = type.DefineMethod(methodName, MethodAttributes.Public);

            List<string> paramNames = new List<string>();

            if (hasGenericReturn)
                paramNames.Add("TReturn");

            var argsStart = paramNames.Count;

            if (hasThis)
                paramNames.Add("TThis");

            for (int arg = 0; arg < numArgs; arg++)
                paramNames.Add($"T{arg + 1}");

            var genericParameters = new Collection<GenericParameter>();

            if (paramNames.Count > 0)
                genericParameters = callMethod.DefineGenericParameters(paramNames.ToArray());

            TypeReference returnTypeOfCall = field.Module.TypeSystem.Void;
            TypeReference returnTypeOfFunc = field.Module.TypeSystem.Void;

            if (hasReturn)
            {
                if (returnType == 1)
                {
                    returnTypeOfCall = genericParameters[0];
                    returnTypeOfFunc = genericParameters[0];
                }
                else if (returnType == 2)
                {
                    genericParameters[0].Attributes |= GenericParameterAttributes.NotNullableValueTypeConstraint
                                                       | GenericParameterAttributes.AllowByRefLikeConstraint;
                    genericParameters[0].Constraints.Add(new GenericParameterConstraint(module.ImportReference(typeof(ValueType))));

                    returnTypeOfCall = genericParameters[0].MakeByRefType();
                    returnTypeOfFunc = genericParameters[0].MakeByRefType();
                }
                else if (returnType == 3)
                {
                    genericParameters[0].Attributes |= GenericParameterAttributes.NotNullableValueTypeConstraint 
                                                       | GenericParameterAttributes.AllowByRefLikeConstraint;
                    
                    returnTypeOfCall = genericParameters[0].MakePointerType();
                    returnTypeOfFunc = genericParameters[0].MakePointerType();
                }
            }

            var genParamTypes = genericParameters.Select(x => (TypeReference)x).ToArray();
            if (hasGenericReturn)
                genParamTypes[0] = returnTypeOfFunc;

            if (thisByRef)
            {
                int id = hasGenericReturn ? 1 : 0;
                genParamTypes[id] = genParamTypes[id].MakeByRefType();
                genericParameters[id].Constraints.Add(new GenericParameterConstraint(module.ImportReference(typeof(ValueType))));
            }
            
            var allArguments = genParamTypes.Length > 0 ? genParamTypes.Skip(argsStart).ToArray() : Array.Empty<TypeReference>();
            if (definition.isUnity)
                allArguments = allArguments.Append(module.ImportReference(typeof(void*))).ToArray();

            callMethod.ReturnType = returnTypeOfFunc;
            callMethod.Parameters.Set(allArguments.Select(ConvertParameter));
            callMethod.ImplAttributes |= MethodImplAttributes.AggressiveInlining;
            
            for (int i = 0; i < allArguments.Length; i++)
            {
                var id = i + 1 - (hasThis ? 1 : 0);
                if (i == 0 && hasThis)
                {
                    callMethod.DefineParameter(1, ParameterAttributes.None, "_this");
                }
                else
                {
                    if (i == allArguments.Length - 1 && definition.isUnity)
                    {
                        var p = callMethod.DefineParameter(i + 1,
                            ParameterAttributes.None | ParameterAttributes.Optional | ParameterAttributes.HasDefault,
                            "runtimeHandleIL2CPP");
                        p.Constant = null;
                    }
                    else
                    {
                        callMethod.DefineParameter(i + 1, ParameterAttributes.None, $"arg{id}");
                    }
                }
            }

            var generator = callMethod.Body.GetILProcessor();
            if (definition.isUnity && !definition.il2cpp)
            {
                generator.DeclareLocal(typeof(ulong));
            }

            // Load this and funcptr field
            if (definition.isUnity && !definition.il2cpp)
            {
                var label1 = generator.DefineLabel();
                generator.Emit(OpCodes.Ldc_I4_1);
                generator.Emit(OpCodes.Conv_I8);
                generator.Emit(OpCodes.Ldc_I4, 63);
                generator.Emit(OpCodes.Shl);
                generator.Emit(OpCodes.Conv_U8);
                generator.Emit(OpCodes.Stloc_0);

                generator.Emit(OpCodes.Ldarg_0);
                generator.Emit(OpCodes.Ldfld, field);
                generator.Emit(OpCodes.Ldloc_0);
                generator.Emit(OpCodes.And);
                generator.Emit(OpCodes.Brtrue_S, label1);

                // Load all arguments
                for (int argId = 0; argId < allArguments.Length - 1; argId++)
                {
                    if (argId == 0)
                        generator.Emit(OpCodes.Ldarg_1);
                    else if (argId == 1)
                        generator.Emit(OpCodes.Ldarg_2);
                    else if (argId == 2)
                        generator.Emit(OpCodes.Ldarg_3);
                    else
                        generator.Emit(OpCodes.Ldarg_S, (byte)(argId + 1));
                }

                var managedArgs = allArguments.Take(allArguments.Length - 1).ToArray();

                generator.Emit(OpCodes.Ldarg_0);
                generator.Emit(OpCodes.Ldfld, field);
                generator.Emit(OpCodes.Conv_U);

                var callSite = new CallSite(returnTypeOfFunc);
                callSite.CallingConvention = MethodCallingConvention.Default;
                callSite.Parameters.Set(managedArgs.Select(ConvertParameter));
                generator.Emit(OpCodes.Calli, callSite);
                //generator.EmitCalli(OpCodes.Calli, CallingConventions.Standard, returnTypeOfFunc, managedArgs, null);
                generator.Emit(OpCodes.Ret);

                generator.MarkLabel(label1);

                // Load all arguments
                for (int argId = 0; argId < allArguments.Length; argId++)
                {
                    if (argId == 0)
                        generator.Emit(OpCodes.Ldarg_1);
                    else if (argId == 1)
                        generator.Emit(OpCodes.Ldarg_2);
                    else if (argId == 2)
                        generator.Emit(OpCodes.Ldarg_3);
                    else
                        generator.Emit(OpCodes.Ldarg_S, (byte)(argId + 1));
                }

                generator.Emit(OpCodes.Ldarg_0);
                generator.Emit(OpCodes.Ldfld, field);
                generator.Emit(OpCodes.Ldloc_0);
                generator.Emit(OpCodes.Not);
                generator.Emit(OpCodes.And);
                generator.Emit(OpCodes.Conv_U);

                var callSite2 = new CallSite(returnTypeOfCall);
                callSite2.CallingConvention = MethodCallingConvention.C;
                callSite2.Parameters.Set(allArguments.Select(ConvertParameter));
                generator.Emit(OpCodes.Calli, callSite2);
                generator.Emit(OpCodes.Ret);
            }
            else if (definition.isUnity && definition.il2cpp)
            {
                // Load all arguments
                for (int argId = 0; argId < allArguments.Length; argId++)
                {
                    if (argId == 0)
                        generator.Emit(OpCodes.Ldarg_1);
                    else if (argId == 1)
                        generator.Emit(OpCodes.Ldarg_2);
                    else if (argId == 2)
                        generator.Emit(OpCodes.Ldarg_3);
                    else
                        generator.Emit(OpCodes.Ldarg_S, (byte)(argId + 1));
                }

                generator.Emit(OpCodes.Ldarg_0);
                generator.Emit(OpCodes.Ldfld, field);

                var callSite = new CallSite(returnTypeOfCall);
                callSite.CallingConvention = MethodCallingConvention.C;
                callSite.Parameters.Set(allArguments.Select(ConvertParameter));
                generator.Emit(OpCodes.Calli, callSite);
                generator.Emit(OpCodes.Ret);
            }
            else
            {
                // Load all arguments
                for (int argId = 0; argId < allArguments.Length; argId++)
                {
                    if (argId == 0)
                        generator.Emit(OpCodes.Ldarg_1);
                    else if (argId == 1)
                        generator.Emit(OpCodes.Ldarg_2);
                    else if (argId == 2)
                        generator.Emit(OpCodes.Ldarg_3);
                    else
                        generator.Emit(OpCodes.Ldarg_S, (byte)(argId + 1));
                }

                generator.Emit(OpCodes.Ldarg_0);
                generator.Emit(OpCodes.Ldfld, field);

                if (definition.IsManaged)
                {
                    var cconv = definition.callingConvention;
                    var callSite = new CallSite(returnTypeOfFunc);
                    callSite.CallingConvention = cconv;
                    callSite.Parameters.Set(allArguments.Select(ConvertParameter));
                    generator.Emit(OpCodes.Calli, callSite);
                }
                else
                {
                    var cconv = definition.callingConvention;
                    
                    var callSite = new CallSite(returnTypeOfCall);
                    callSite.CallingConvention = cconv;
                    callSite.Parameters.Set(allArguments.Select(ConvertParameter));
                    generator.Emit(OpCodes.Calli, callSite);
                }

                generator.Emit(OpCodes.Ret);
            }

            return callMethod;
        }

        private static ParameterDefinition ConvertParameter(TypeReference arg, int i)
        {
            return new ParameterDefinition($"_p{i}", ParameterAttributes.None, arg);
        }

        private static string GetMethodNameForType(Type returnType)
        {
            if (returnType == typeof(sbyte))
                return "Char";

            if (returnType == typeof(byte))
                return "Byte";

            if (returnType == typeof(short))
                return "Int16";

            if (returnType == typeof(ushort))
                return "UInt16";

            if (returnType == typeof(int))
                return "Int32";

            if (returnType == typeof(uint))
                return "UInt32";

            if (returnType == typeof(long))
                return "Int64";

            if (returnType == typeof(ulong))
                return "UInt64";

            if (returnType == typeof(IntPtr) || returnType == typeof(UIntPtr) || returnType.IsPointer)
                return "IntPtr";

            if (returnType == typeof(string))
                return "String";

            if (returnType == typeof(object))
                return "Object";

            if (returnType == typeof(float))
                return "Float";
            
            if (returnType == typeof(double))
                return "Double";

            throw new KeyNotFoundException($"Can't find method name for type {returnType}");
        }

        public class ModStruct
        {
            public TypeReference type;
            public FieldReference methodPtr;
            public ModDefinition definition;
        }

        public static void Main(string[] args)
        {
            var an = new AssemblyNameDefinition("ILCall", new Version(1,0,0));
            dynamicAssembly = AssemblyDefinition.CreateAssembly(an, "ILCall.dll", new ModuleParameters()
            {
                Kind = ModuleKind.Dll
            });

            module = dynamicAssembly.MainModule;
            var invalidOpEx = module.ImportReference(typeof(InvalidOperationException)).Resolve();
            invalidOperationExceptionConstructor = invalidOpEx.GetConstructors()
                .First(x =>
                    x.Parameters.Count == 1 && x.Parameters[0].ParameterType.TypeEquals(module.TypeSystem.String));
            invalidOperationExceptionConstructor = module.ImportReference(invalidOperationExceptionConstructor);
            
            var managed = AddModType(new ModDefinition()
            {
                hasThis = true,
                callingConvention = MethodCallingConvention.Default,
                name = "Managed"
            });

            var managedStatic = AddModType(new ModDefinition()
            {
                hasThis = false,
                callingConvention = MethodCallingConvention.Default,
                name = "Static"
            });

            var native = AddModType(new ModDefinition()
            {
                hasThis = false,
                callingConvention = MethodCallingConvention.Unmanaged, // should be a special type
                name = "Native"
            });

            var cdecl = AddModType(new ModDefinition()
            {
                hasThis = false,
                callingConvention = MethodCallingConvention.C,
                name = "Cdecl"
            });

            var stdcall = AddModType(new ModDefinition()
            {
                hasThis = false,
                callingConvention = MethodCallingConvention.StdCall,
                name = "StdCall"
            });

            var thiscall = AddModType(new ModDefinition()
            {
                hasThis = true,
                callingConvention = MethodCallingConvention.ThisCall,
                name = "ThisCall"
            });

            var unity = AddModType(new ModDefinition()
            {
                hasThis = true,
                callingConvention = MethodCallingConvention.C,
                isUnity = true,
                name = "Unity"
            });

            var il2cpp = AddModType(new ModDefinition()
            {
                hasThis = true,
                callingConvention = MethodCallingConvention.C,
                isUnity = true,
                il2cpp = true,
                name = "IL2CPP"
            });

            var il2cppStatic = AddModType(new ModDefinition()
            {
                hasThis = false,
                callingConvention = MethodCallingConvention.C,
                isUnity = true,
                il2cpp = true,
                name = "IL2CPPStatic"
            });

            var unityStatic = AddModType(new ModDefinition()
            {
                hasThis = false,
                callingConvention = MethodCallingConvention.C,
                isUnity = true,
                name = "UnityStatic"
            });

            var allTypes = new ModStruct[]
            {
                managed, managedStatic, native, unity, unityStatic, cdecl, stdcall, thiscall, il2cpp, il2cppStatic
            };

            GenerateToIL2CPP(unity, il2cpp);
            GenerateToIL2CPP(unityStatic, il2cppStatic);
            GenerateToManaged(unity, il2cpp);
            GenerateToManaged(unityStatic, il2cppStatic);

            foreach (var current in allTypes)
            {
                foreach (var other in allTypes)
                {
                    if (other == current)
                        continue;

                    if (current == unity
                        && other != unityStatic)
                        continue;

                    if (current == unityStatic
                        && other != unity)
                        continue;

                    if (current == il2cpp
                        && other != il2cppStatic)
                        continue;

                    if (current == il2cppStatic
                        && other != il2cpp)
                        continue;

                    //TODO: Add type conversion.
                    var convMethod =
                        current.type.DefineMethod($"As{other.type.Name.Substring(4)}", MethodAttributes.Public);
                    convMethod.ReturnType = other.type;
                    convMethod.Body.InitLocals = false;
                    
                    var gen = convMethod.Body.GetILProcessor();

                    gen.DeclareLocal(other.type);
                    gen.Emit(OpCodes.Ldloca_S, (byte)0);
                    gen.Emit(OpCodes.Ldarg_0);
                    gen.Emit(OpCodes.Ldfld, current.methodPtr);
                    gen.Emit(OpCodes.Stfld, other.methodPtr);

                    gen.Emit(OpCodes.Ldloc_0);
                    gen.Emit(OpCodes.Ret);
                }
            }

            /*
            foreach (var typeBuilder in allTypes)
            {
                module.Types.Add(typeBuilder.type.Resolve());
            }
            */

            dynamicAssembly.Write("ILCall.dll");
        }

        private static unsafe void GenerateToIL2CPP(ModStruct addToType, ModStruct toType)
        {
            var asil2cpp = addToType.type.DefineMethod("AsIL2CPP", MethodAttributes.Public);
            asil2cpp.ReturnType = toType.type;
            var g = asil2cpp.Body.GetILProcessor();
            var m1 = g.DeclareLocal(typeof(ulong));
            var m2 = g.DeclareLocal(typeof(IntPtr));
            var newStruct = g.DeclareLocal(toType.type);
            var label = g.DefineLabel();

            g.Emit(OpCodes.Ldc_I4_1);
            g.Emit(OpCodes.Conv_I8);
            g.Emit(OpCodes.Ldc_I4_S, (sbyte)63);
            g.Emit(OpCodes.Shl);
            g.Emit(OpCodes.Conv_U8);
            g.Emit(OpCodes.Stloc_0);
            g.Emit(OpCodes.Ldloc_0);
            g.Emit(OpCodes.Ldarg_0);
            g.Emit(OpCodes.Ldfld, addToType.methodPtr);
            g.Emit(OpCodes.And);
            g.Emit(OpCodes.Brtrue_S, label);
            g.Emit(OpCodes.Ldstr, "This is not IL2CPP direct call.");
            g.Emit(OpCodes.Newobj, invalidOperationExceptionConstructor);
            g.Emit(OpCodes.Throw);
            g.MarkLabel(label);
            //g.Emit(OpCodes.Ldloca_S, newStruct);
            //g.Emit(OpCodes.Initobj, toType.type);

            // mask
            g.Emit(OpCodes.Ldarg_0);
            g.Emit(OpCodes.Ldfld, addToType.methodPtr);
            g.Emit(OpCodes.Ldloc_0);
            g.Emit(OpCodes.Not);
            g.Emit(OpCodes.And);
            g.Emit(OpCodes.Conv_I);
            g.Emit(OpCodes.Stloc_1);

            g.Emit(OpCodes.Ldloca_S, newStruct);
            g.Emit(OpCodes.Ldloc_1);
            g.Emit(OpCodes.Stfld, toType.methodPtr);
            g.Emit(OpCodes.Ldloc_S, newStruct);
            g.Emit(OpCodes.Ret);
        }

        private static unsafe void GenerateToManaged(ModStruct addToType, ModStruct toType)
        {
            var asil2cpp = addToType.type.DefineMethod("AsManaged", MethodAttributes.Public);
            asil2cpp.ReturnType = toType.type;
            var g = asil2cpp.Body.GetILProcessor();
            
            var m1 = g.DeclareLocal(typeof(ulong));
            var m2 = g.DeclareLocal(typeof(IntPtr));
            var newStruct = g.DeclareLocal(toType.type);
            var label = g.DefineLabel();

            g.Emit(OpCodes.Ldc_I4_1);
            g.Emit(OpCodes.Conv_I8);
            g.Emit(OpCodes.Ldc_I4_S, (sbyte)63);
            g.Emit(OpCodes.Shl);
            g.Emit(OpCodes.Conv_U8);
            g.Emit(OpCodes.Stloc_0);
            g.Emit(OpCodes.Ldloc_0);
            g.Emit(OpCodes.Ldarg_0);
            g.Emit(OpCodes.Ldfld, addToType.methodPtr);
            g.Emit(OpCodes.And);
            g.Emit(OpCodes.Brfalse, label);
            g.Emit(OpCodes.Ldstr, "This is IL2CPP direct call.");
            g.Emit(OpCodes.Newobj, invalidOperationExceptionConstructor);
            g.Emit(OpCodes.Throw);
            g.MarkLabel(label);
            //g.Emit(OpCodes.Ldloca_S, newStruct);
            //g.Emit(OpCodes.Initobj, toType.type);

            // mask
            g.Emit(OpCodes.Ldarg_0);
            g.Emit(OpCodes.Ldfld, addToType.methodPtr);
            g.Emit(OpCodes.Ldloc_0);
            g.Emit(OpCodes.Not);
            g.Emit(OpCodes.And);
            g.Emit(OpCodes.Conv_I);
            g.Emit(OpCodes.Stloc_1);

            g.Emit(OpCodes.Ldloca_S, newStruct);
            g.Emit(OpCodes.Ldloc_1);
            g.Emit(OpCodes.Stfld, toType.methodPtr);
            g.Emit(OpCodes.Ldloc_S, newStruct);
            g.Emit(OpCodes.Ret);
        }

        /*
        private static void GenerateRefReturnType()
        {
            refReturnType = module.DefineType("RefReturn", TypeAttributes.Public | TypeAttributes.SequentialLayout,
                typeof(ValueType));
            var rfield = refReturnType.DefineField("_ptr", typeof(IntPtr), FieldAttributes.Private);

            var asMethod = refReturnType.DefineMethod("As", MethodAttributes.Public);
            var tret = asMethod.DefineGenericParameters("T");

            asMethod.SetReturnType(tret[0].MakeByRefType());
            var gt = asMethod.GetILGenerator();
            gt.Emit(OpCodes.Ldarg_0);
            gt.Emit(OpCodes.Ldfld, rfield);
            gt.Emit(OpCodes.Ret);
            refReturnType.CreateType();
        }

        private static void GenerateFunctionWithReturnType(TypeBuilder type, Type retType, Type[] argTypes, int numArgs)
        {
        }
        */
    }
}