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

        private static ModuleDefinition module;
        private static AssemblyDefinition dynamicAssembly;
        private static MethodReference invalidOperationExceptionConstructor;
        private static TypeReference ValueTypeImported;
        private static TypeReference VoidPtrImported;
        
        private static bool net35 = true;
        
        //private static TypeBuilder refReturnType;

        public enum ReturnTypeEnum
        {
            Void = 0,
            Generic,
            GenericRef,
            GenericPtr,
            Custom,
        }
        
        public static ModStruct AddModType(ModDefinition definition)
        {
            var type = module.DefineType("", $"Func{definition.name}", TypeAttributes.Public | TypeAttributes.SequentialLayout);
            type.BaseType = ValueTypeImported;
            module.Types.Add(type);

            FieldDefinition funcPtrField = null;
            if (definition.isUnity && !definition.il2cpp)
                funcPtrField = type.DefineField("methodPtr", module.TypeSystem.UInt64, FieldAttributes.Public);
            else
                funcPtrField = type.DefineField("methodPtr", module.TypeSystem.IntPtr, FieldAttributes.Public);

            var retTypes = new List<ReturnTypeEnum>();
            retTypes.Add(ReturnTypeEnum.Void);
            retTypes.Add(ReturnTypeEnum.Generic);
            retTypes.Add(ReturnTypeEnum.GenericPtr);
            
            if(!net35)
                retTypes.Add(ReturnTypeEnum.GenericRef);
            
            retTypes.Add(ReturnTypeEnum.Custom);

            const int maxNumArgs = 8;
            for (int i = 0; i <= maxNumArgs; i++)
            {
                string methodNameBase = "";

                foreach (var retType in retTypes)
                {
                    if (retType == ReturnTypeEnum.Custom)
                    {
                        for (var index = 0; index < Names.ImportedTypes.Length; index++)
                        {
                            GenerateMethods(definition, retType, index, funcPtrField, type, i);
                        }
                    }
                    else
                    {
                        GenerateMethods(definition, retType, 0, funcPtrField, type, i);
                    }
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
                
                initMethod.Parameters.Add(new ParameterDefinition("funcPtr", ParameterAttributes.None, module.TypeSystem.IntPtr));
                initMethod.Parameters.Add(new ParameterDefinition("isIL2CPPDirect", ParameterAttributes.Optional | ParameterAttributes.HasDefault, module.TypeSystem.Boolean));
                initMethod.Parameters[initMethod.Parameters.Count - 1].Constant = false;
                initMethod.ImplAttributes |= MethodImplAttributes.AggressiveInlining;
                
                var il = initMethod.Body.GetILProcessor();
                il.DeclareLocal(module.TypeSystem.UInt64);
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

        private static void GenerateMethods(ModDefinition definition, ReturnTypeEnum retType, int customTypeId, FieldDefinition funcPtrField,
            TypeDefinition type, int i)
        {
            string methodName = null;
            methodName = GetMethodNameForReturn(retType, customTypeId);
            var customType = retType == ReturnTypeEnum.Custom ? Names.ImportedTypes[customTypeId] : null;
            
            if (definition.hasThis)
            {
                AddMethod("This" + methodName, true, false, funcPtrField, definition, type, retType, customType, i);
                AddMethod("This" + methodName, true, true, funcPtrField, definition, type, retType, customType, i);
                AddMethod(methodName, false, false, funcPtrField, definition, type, retType, customType, i);
            }
            else
            {
                AddMethod(methodName, false, false, funcPtrField, definition, type, retType, customType, i);
            }
        }

        private static string GetMethodNameForReturn(ReturnTypeEnum retType, int customTypeId)
        {
            switch (retType)
            {
                case ReturnTypeEnum.Void:
                    return "Void";
                case ReturnTypeEnum.Generic:
                    return "Generic";
                case ReturnTypeEnum.GenericRef:
                    return "Ref";
                case ReturnTypeEnum.GenericPtr:
                    return "Ptr";
                case ReturnTypeEnum.Custom:
                    return Names.TypeNames[customTypeId];
                default:
                    return "Invalid";
            }
        }

        private static unsafe MethodDefinition AddMethod(string methodName, bool hasThis, bool thisByRef, FieldDefinition field, ModDefinition definition, TypeDefinition type,
            ReturnTypeEnum returnType, TypeReference customReturnType, int numArgs)
        {
            bool hasReturn = returnType != 0;
            bool hasGenericReturn = hasReturn && customReturnType == null;
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

            if (hasGenericReturn)
            {
                if (returnType == ReturnTypeEnum.Generic)
                {
                    returnTypeOfCall = genericParameters[0];
                    returnTypeOfFunc = genericParameters[0];
                }
                else if (returnType == ReturnTypeEnum.GenericRef)
                {
                    genericParameters[0].Attributes |= GenericParameterAttributes.NotNullableValueTypeConstraint
                                                       | GenericParameterAttributes.AllowByRefLikeConstraint;
                    genericParameters[0].Constraints.Add(new GenericParameterConstraint(ValueTypeImported));

                    returnTypeOfCall = genericParameters[0].MakeByRefType();
                    returnTypeOfFunc = genericParameters[0].MakeByRefType();
                }
                else if (returnType == ReturnTypeEnum.GenericPtr)
                {
                    genericParameters[0].Attributes |= GenericParameterAttributes.NotNullableValueTypeConstraint 
                                                       | GenericParameterAttributes.AllowByRefLikeConstraint;
                    
                    returnTypeOfCall = genericParameters[0].MakePointerType();
                    returnTypeOfFunc = genericParameters[0].MakePointerType();
                }
            }
            else if(hasReturn)
            {
                returnTypeOfFunc = customReturnType;
                returnTypeOfCall = customReturnType;
                
                if (customReturnType.IsPointer || !customReturnType.IsValueType)
                {
                    // demote to the pointer.
                    returnTypeOfCall = module.TypeSystem.IntPtr;
                }
            }

            var genParamTypes = genericParameters.Select(x => (TypeReference)x).ToArray();
            if (hasGenericReturn)
                genParamTypes[0] = returnTypeOfFunc;

            if (thisByRef)
            {
                int id = hasGenericReturn ? 1 : 0;
                genParamTypes[id] = genParamTypes[id].MakeByRefType();
                genericParameters[id].Constraints.Add(new GenericParameterConstraint(ValueTypeImported));
                genericParameters[id].Attributes |= GenericParameterAttributes.NotNullableValueTypeConstraint 
                                                   | GenericParameterAttributes.AllowByRefLikeConstraint;
            }
            
            var allArguments = genParamTypes.Length > 0 ? genParamTypes.Skip(argsStart).ToArray() : Array.Empty<TypeReference>();
            if (definition.isUnity)
                allArguments = allArguments.Append(VoidPtrImported).ToArray();

            callMethod.ReturnType = returnTypeOfFunc;
            //callMethod.Parameters.Set(allArguments.Select(ConvertParameter));
            callMethod.ImplAttributes |= MethodImplAttributes.AggressiveInlining;

            for (int i = 0; i < allArguments.Length; i++)
            {
                var id = i + 1 - (hasThis ? 1 : 0);
                if (i == 0 && hasThis)
                {
                    callMethod.Parameters.Add(new ParameterDefinition("_this", ParameterAttributes.None, allArguments[0]));
                    //callMethod.DefineParameter(1, ParameterAttributes.None, "_this");
                }
                else
                {
                    if (i == allArguments.Length - 1 && definition.isUnity)
                    {
                        var prm = new ParameterDefinition("runtimeHandleIL2CPP", ParameterAttributes.Optional | ParameterAttributes.HasDefault,
                            VoidPtrImported);
                        prm.Constant = null;
                        callMethod.Parameters.Add(prm);
                    }
                    else
                    {
                        callMethod.Parameters.Add(new ParameterDefinition($"arg{id}", ParameterAttributes.None, allArguments[i]));
                        //callMethod.DefineParameter(i + 1, ParameterAttributes.None, $"arg{id}");
                    }
                }
            }

            var generator = callMethod.Body.GetILProcessor();
            if (definition.isUnity && !definition.il2cpp)
            {
                generator.DeclareLocal(module.TypeSystem.UInt64);
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
                callSite2.CallingConvention = definition.callingConvention;
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
                callSite.CallingConvention = definition.callingConvention;
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

        public class ModStruct
        {
            public TypeReference type;
            public FieldReference methodPtr;
            public ModDefinition definition;
        }

        private static DefaultAssemblyResolver Resolver;
        static ModuleDefinition LoadNetstandard10()
        {
            var netstdRef = new AssemblyNameReference("mscorlib", new Version(2, 0, 0, 0));

            var asm = Resolver.Resolve(netstdRef);   // this uses added search dirs
            return asm.MainModule;
        }
        
        public static void Main(string[] args)
        {
            
            //var loadedAsssembly = AssemblyDefinition.ReadAssembly("C:\\Users\\alex\\Documents\\GitHub\\Bakanov\\MadSharpUtils\\MadUnsafe\\ILCall.dll");
            //var typeCdecl = loadedAsssembly.MainModule.Types.First(x => x.Name.Equals("FuncCdecl")).Methods.First(x => x.Name.Equals("Generic") && x.Parameters.Count == 2);
            //var typeStdcall = loadedAsssembly.MainModule.Types.First(x => x.Name.Equals("FuncStdCall")).Methods
            //    .First(x => x.Name.Equals("Generic") && x.Parameters.Count == 2);
            //var typeNative = loadedAsssembly.MainModule.Types.First(x => x.Name.Equals("FuncNative")).Methods
            //    .First(x => x.Name.Equals("Generic") && x.Parameters.Count == 2);
            //
            //loadedAsssembly.Dispose();
            Resolver = new DefaultAssemblyResolver();
            //resolver.AddSearchDirectory(Environment.CurrentDirectory);
            var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory() + "/../";
            Resolver.AddSearchDirectory(runtimeDir);
            
            Console.WriteLine($"RuntimeDir = {RuntimeEnvironment.GetRuntimeDirectory()}");
            
            var an = new AssemblyNameDefinition("ILCall", new Version(1,0,0));
            dynamicAssembly = AssemblyDefinition.CreateAssembly(an, "ILCall.dll", new ModuleParameters()
            {
                Kind = ModuleKind.Dll,
                Runtime = TargetRuntime.Net_2_0,
                AssemblyResolver = Resolver
            });

            module = dynamicAssembly.MainModule;
            
            //if(net35)
            //module.RuntimeVersion = "v2.0.50727";
            
            var netstandard = LoadNetstandard10();
            Console.WriteLine($"Core library: {netstandard.FullyQualifiedName} ({netstandard.RuntimeVersion})");
            
            //foreach (var netstandardType in netstandard.Types)
            //{
            //    Console.WriteLine($"StandardType: {netstandardType.Name}");
            //}
            
            var invalidOpEx =
                netstandard.Types.First(x => x.Name.Equals("InvalidOperationException", StringComparison.Ordinal));
            invalidOperationExceptionConstructor = invalidOpEx.GetConstructors()
                .First(x =>
                    x.Parameters.Count == 1 && x.Parameters[0].ParameterType.TypeEquals(module.TypeSystem.String));
            invalidOperationExceptionConstructor = module.ImportReference(invalidOperationExceptionConstructor);

            ValueTypeImported = module.ImportReference(netstandard.Types.First(x => x.Name.Equals("ValueType", StringComparison.Ordinal)));
            VoidPtrImported = module.TypeSystem.Void.MakePointerType();
            
            Names.ImportedTypes = Names.TypeProtos.Select(typeProto =>
            {
                return module.ImportReference(netstandard.Types.First(x =>
                    x.Name.Equals(typeProto.Name, StringComparison.Ordinal)));
            }).ToArray();
            
            var managed = AddModType(new ModDefinition()
            {
                hasThis = true,
                callingConvention = MethodCallingConvention.Default,
                name = "Managed"
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
                callingConvention = MethodCallingConvention.Unmanaged, // platform cconv
                isUnity = true,
                il2cpp = true,
                name = "IL2CPP"
            });

            var allTypes = new ModStruct[]
            {
                managed, native, unity, cdecl, stdcall, thiscall, il2cpp
            };

            GenerateToIL2CPP(unity, il2cpp);
            //GenerateToIL2CPP(unityStatic, il2cppStatic);
            GenerateToManaged(unity, il2cpp);
            //GenerateToManaged(unityStatic, il2cppStatic);

            ModStruct unityStatic = null;
            ModStruct il2cppStatic = null;
            
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
            var m1 = g.DeclareLocal(module.TypeSystem.UInt64);
            var m2 = g.DeclareLocal(module.TypeSystem.IntPtr);
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
            
            var m1 = g.DeclareLocal(module.TypeSystem.UInt64);
            var m2 = g.DeclareLocal(module.TypeSystem.IntPtr);
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