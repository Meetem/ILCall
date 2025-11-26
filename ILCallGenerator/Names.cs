using System;
using System.Collections.Generic;
using Mono.Cecil;

namespace ConsoleApplication2
{
    public static class Names
    {
        public static Type[] TypeProtos = new Type[]
        {
            typeof(char),
            typeof(byte),
            typeof(short),
            typeof(int),
            typeof(long),
            typeof(IntPtr),
            typeof(string),
            typeof(object),
            typeof(Array),
        };

        public static TypeReference[] ImportedTypes;
        
        public static string[] TypeNames = new string[]
        {
            "Char",
            "Byte",
            "Int16",
            "Int32",
            "Int64",
            "IntPtr",
            "String",
            "Object",
            "Array"
        };

    }
}