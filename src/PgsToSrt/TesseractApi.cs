using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using TesseractOCR.Interop;
using Tncl.NativeLoader;

namespace PgsToSrt
{
    static class TesseractApi
    {
        private const string _assemblyName = "Assembly.Tesseract";

        public static Exception Initialize(string tesseractVersion, string libLeptName, string libLeptVersion)
        {
            Exception exception = null;

            if (string.IsNullOrEmpty(libLeptName))
                libLeptName = "lept";

            if (string.IsNullOrEmpty(libLeptVersion))
                libLeptVersion = "5";

            var tessApiType = typeof(TesseractOCR.Page).Assembly.GetType("TesseractOCR.Interop.TessApi");
            var leptApiType = typeof(TesseractOCR.Page).Assembly.GetType("TesseractOCR.Interop.LeptonicaApi");

            var tessApiCustomType = CreateInterfaceType<ITessApiSignatures>("tesseract53", "tesseract", tesseractVersion);
            var leptApiCustomType = CreateInterfaceType<ILeptonicaApiSignatures>("leptonica-1.83.1", libLeptName, libLeptVersion);

            var loader = new NativeLoader();
            loader.WindowsOptions.UseSetDllDirectory = true;

            try
            {
                var tessApiInstance = (ITessApiSignatures)loader.CreateInstance(tessApiCustomType);
                var leptApiInstance = (ILeptonicaApiSignatures)loader.CreateInstance(leptApiCustomType);
            
                tessApiType.GetField("native", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, tessApiInstance);
                leptApiType.GetField("native", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, leptApiInstance);
            }
            catch (TargetInvocationException e)
            {
                exception = e.InnerException;
            }
            catch (Exception e)
            {
                exception = e;
            }

            return exception;
        }

        public static Type CreateInterfaceType<T>(string windowsLibraryName, string commonLibraryName, string version)
        {
            var interfaceType = typeof(T);
            var typeName = $"{_assemblyName}.{interfaceType.Name}2";
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(_assemblyName), AssemblyBuilderAccess.Run);
            var moduleBuilder = assemblyBuilder.DefineDynamicModule(_assemblyName);
            var typeBuilder = moduleBuilder.DefineType(typeName, TypeAttributes.Interface | TypeAttributes.Abstract | TypeAttributes.Public, null, new Type[1] { interfaceType });
            var tesseractMethods = GetTesseractMethods(interfaceType);

            foreach (var tesseractMethod in tesseractMethods)
            {
                var methodInfo = tesseractMethod.Key;
                var parametersType = from p in methodInfo.GetParameters() select p.ParameterType;

                var methodBuilder = typeBuilder.DefineMethod(
                    methodInfo.Name, methodInfo.Attributes,
                    methodInfo.CallingConvention, methodInfo.ReturnType, parametersType.ToArray());

                var nativeLoaderOverrideAttributeProperties = new Dictionary<string, object>();
                nativeLoaderOverrideAttributeProperties.Add("LibraryName", windowsLibraryName);
                nativeLoaderOverrideAttributeProperties.Add("Platform", Platform.Windows);
                methodBuilder.AddCustomAttribute<NativeLoaderOverrideAttribute>(nativeLoaderOverrideAttributeProperties);

                var runtimeUnmanagedFunctionPointerAttributeProperties = new Dictionary<string, object>();
                runtimeUnmanagedFunctionPointerAttributeProperties.Add("LibraryName", commonLibraryName);
                runtimeUnmanagedFunctionPointerAttributeProperties.Add("LibraryVersion", version);
                runtimeUnmanagedFunctionPointerAttributeProperties.Add("CallingConvention", CallingConvention.Cdecl);
                runtimeUnmanagedFunctionPointerAttributeProperties.Add("EntryPoint", tesseractMethod.Value);
                methodBuilder.AddCustomAttribute<RuntimeUnmanagedFunctionPointerAttribute>(runtimeUnmanagedFunctionPointerAttributeProperties);
            }

            return typeBuilder.CreateType();
        }

        public static List<KeyValuePair<MethodInfo, string>> GetTesseractMethods(Type type)
        {
            var result = new List<KeyValuePair<MethodInfo, string>>();
            var runtimeDllImportAttributeType = typeof(TesseractOCR.Page).Assembly.GetType("TesseractOCR.InteropDotNet.RuntimeDllImportAttribute");
            
            foreach (var methodInfo in type.GetMethods())
            {
                var runtimeDllImportAttribute = methodInfo.GetCustomAttribute(runtimeDllImportAttributeType);

                if (runtimeDllImportAttribute != null)
                {
                    var entryPoint = (string)runtimeDllImportAttribute.GetType().GetField("EntryPoint").GetValue(runtimeDllImportAttribute);
                    result.Add(new KeyValuePair<MethodInfo, string>(methodInfo, entryPoint));
                }
            }

            return result;
        }

        public static void AddCustomAttribute<T>(this MethodBuilder methodBuilder, Dictionary<string, object> propertiesNameValue)
        {
            var attributeType = typeof(T);
            var attributeConstructorInfo = attributeType.GetConstructor(new Type[0] { });
            var (propertyInfos, propertyValues) = SplitPropertiesNameValue(attributeType, propertiesNameValue);
            var attributeBuilder = new CustomAttributeBuilder(attributeConstructorInfo, new object[0] { }, propertyInfos, propertyValues);

            methodBuilder.SetCustomAttribute(attributeBuilder);
        }

        public static (PropertyInfo[] propertyInfos, object[] propertyValues) SplitPropertiesNameValue(Type type, Dictionary<string, object> propertiesNameValue)
        {
            var propertyInfos = new List<PropertyInfo>();
            var propertyValues = new List<object>();

            foreach (var item in propertiesNameValue)
            {
                var propertyName = item.Key;
                var propertyValue = item.Value;

                var property = type.GetProperty(propertyName);
                propertyInfos.Add(property);
                propertyValues.Add(propertyValue);
            }

            return (propertyInfos.ToArray(), propertyValues.ToArray());
        }

    }
}
