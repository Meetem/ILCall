using System;
using System.Collections.Generic;
using System.Runtime.Remoting.Contexts;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace ConsoleApplication2
{
    public static class MonoCecilExt
    {
        public static MethodDefinition DefineMethod(this TypeReference type, string name, MethodAttributes attributes, TypeReference returnType = null)
        {
            if (returnType == null)
                returnType = type.Module.TypeSystem.Void;

            var me = new MethodDefinition(name, attributes, returnType);
            type.Resolve().Methods.Add(me);
            return me;
        }

        public static bool TypeEquals(this TypeReference a, TypeReference b)
        {
            if (ReferenceEquals(a, b))
                return true;
            
            if (a == b)
                return true;
            
            return a?.FullName?.Equals(b?.FullName ?? null, StringComparison.Ordinal) ?? false;
        }
        
        public static TypeDefinition DefineType(this ModuleDefinition module, string @namespace, string name, TypeAttributes attributes)
        {
            var td = new TypeDefinition(@namespace, name, attributes);
            return td;
        }

        public static FieldDefinition DefineField(this TypeDefinition typedef, string name, Type fieldType, FieldAttributes attributes)
        {
            var td = new FieldDefinition(name, attributes, typedef.Module.ImportReference(fieldType));
            typedef.Fields.Add(td);
            return td;
        }

        public static FieldDefinition DefineField(this TypeDefinition typedef, string name, TypeReference fieldType, FieldAttributes attributes)
        {
            var td = new FieldDefinition(name, attributes, typedef.Module.ImportReference(fieldType));
            typedef.Fields.Add(td);
            return td;
        }

        public struct ILLabel
        {
            public Instruction instruction;

            public ILLabel(Instruction location)
            {
                this.instruction = location;
            }

            public static implicit operator Instruction(ILLabel label) => label.instruction;
        }
        
        public static ILLabel DefineLabel(this ILProcessor il)
        {
            return new ILLabel(il.Create(OpCodes.Nop));
        }
        
        public static void MarkLabel(this ILProcessor il, ILLabel instr)
        {
            il.Append(instr.instruction);
        }

        public static void Set<T>(this Collection<T> collection, IEnumerable<T> source)
        {
            if (collection == null)
                return;
            
            collection.Clear();
            if (source != null)
            {
                foreach (var s in source)
                    collection.Add(s);
            }
        }
        
        public static VariableDefinition DeclareLocal(this ILProcessor il, Type t)
        {
            var method = il.Body.Method;
            var typeRef = method.Module.ImportReference(t);
            var vd = new VariableDefinition(typeRef);
            method.Body.Variables.Add(vd);
            return vd;
        }
        
        public static VariableDefinition DeclareLocal(this ILProcessor il, TypeReference t)
        {
            var method = il.Body.Method;
            var typeRef = method.Module.ImportReference(t);
            var vd = new VariableDefinition(typeRef);
            method.Body.Variables.Add(vd);
            return vd;
        }

        public static ParameterDefinition DefineParameter(this MethodDefinition method, int idx, ParameterAttributes attributes, string name)
        {
            var c = method.Parameters;
            while (c.Count <= idx)
                c.Add(new ParameterDefinition($"_{c.Count}", ParameterAttributes.None, method.Module.TypeSystem.Void));

            var paramAt = c[idx];
            var pd = new ParameterDefinition(name, attributes, method.Module.TypeSystem.Void);
            if (paramAt == null)
            {
                c[idx] = pd;
                return pd;
            }

            pd.Name = name;
            pd.Attributes = attributes;
            c[idx] = pd;
            return pd;
        }
        
        public static Collection<GenericParameter> DefineGenericParameters(this MethodDefinition method, IEnumerable<string> parameters)
        {
            foreach (string p in parameters)
            {
                method.GenericParameters.Add(new GenericParameter(p, method));
            }

            return method.GenericParameters;
        }

        public static TypeReference MakePointerType(this TypeReference type)
        {
            return new PointerType(type);
        }
        
        public static TypeReference MakeByRefType(this TypeReference type)
        {
            return new ByReferenceType(type);
        }
    }
}