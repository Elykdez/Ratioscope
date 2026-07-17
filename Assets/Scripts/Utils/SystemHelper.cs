using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Hypocycloid.Utils
{
    [Flags]
    public enum Comparison
    {
        Equal = 0b1,
        Greater = 0b10,
        Less = 0b100,
    }

    public static class SystemHelper
    {
        // static readonly string DefaultNamespace = typeof(SystemHelper).Namespace;
        const string NS_NAME = "Hypocycloid.AIP";

        // static readonly string DefaultAssemblyName = typeof(SystemHelper).Assembly.GetName().Name;
        const string DEF_ASSEMBLY_NAME = "Assembly-CSharp";

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        const int SW_SHOWNORMAL = 1;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr ShellExecuteW(
            IntPtr hwnd,
            string lpOperation,
            string lpFile,
            string lpParameters,
            string lpDirectory,
            int nShowCmd
        );
#endif

        #region Process

        /// <summary>
        /// Reveals the specified file or directory in the system's default file explorer.
        /// </summary>
        /// <param name="path">The full path to the file or directory to be revealed.</param>
        public static void RevealFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                LogHelper.LogWarning("Cannot reveal an empty path.");
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            LogHelper.LogWarning("File reveal is not supported on WebGL.");
#else
            try
            {
                string normalizedPath = Path.GetFullPath(path);
                bool isDirectory = Directory.Exists(normalizedPath);
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                normalizedPath = normalizedPath.Replace("/", "\\");
                RevealOnWindows(normalizedPath, isDirectory);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
                string arguments = isDirectory
                    ? QuoteArgument(normalizedPath)
                    : $"-R {QuoteArgument(normalizedPath)}";
                TryStartProcess("/usr/bin/open", arguments);
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
                string targetPath = isDirectory
                    ? normalizedPath
                    : Path.GetDirectoryName(normalizedPath);
                if (string.IsNullOrEmpty(targetPath))
                {
                    LogHelper.LogWarning(
                        $"Cannot reveal path because its parent folder was not found: {path}"
                    );
                    return;
                }

                TryStartProcess("xdg-open", QuoteArgument(targetPath));
#else
                LogHelper.LogWarning("Unsupported platform for file reveal.");
#endif
            }
            catch (Exception ex)
            {
                LogHelper.LogWarning($"Failed to reveal path '{path}': {ex.Message}");
            }
#endif
        }

        /// <summary>
        /// Starts a process with the specified file name and arguments.
        /// Rooted file names are checked before launch; command names are left for the platform to resolve.
        /// UseShellExecute = true when we want shell behavior, like opening a URL or file by association.
        /// UseShellExecute = false when we already know the executable and are passing explicit arguments.
        /// </summary>
        public static bool TryStartProcess(
            string fileName,
            string arguments,
            bool useShellExecute = false
        )
        {
            return TryStartProcess(
                new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments ?? string.Empty,
                    UseShellExecute = useShellExecute,
                },
                out _
            );
        }

        /// <summary>
        /// Starts a process from a fully-constructed <see cref="ProcessStartInfo"/>.
        /// Use this overload when you need ArgumentList, WorkingDirectory, or want the caller
        /// to surface the launch failure (e.g. via UI) — the failure reason is returned via <paramref name="error"/>.
        /// </summary>
        public static bool TryStartProcess(ProcessStartInfo info, out string error)
        {
            error = null;
#if UNITY_WEBGL && !UNITY_EDITOR
            LogHelper.LogWarning("Process launching is not supported on WebGL.");
            return false;
#else
            if (info is null || string.IsNullOrWhiteSpace(info.FileName))
            {
                error = "file name is empty";
                LogHelper.LogWarning("Cannot start process because file name is empty.");
                return false;
            }

            try
            {
                if (Path.IsPathRooted(info.FileName) && !File.Exists(info.FileName))
                {
                    error = $"file not found: {info.FileName}";
                    LogHelper.LogWarning(
                        $"Cannot start process because file was not found: {info.FileName}"
                    );
                    return false;
                }

                Process.Start(info);
                return true;
            }
            catch (Exception ex)
                when (ex
                        is Win32Exception
                            or InvalidOperationException
                            or FileNotFoundException
                            or DirectoryNotFoundException
                            or IOException
                            or UnauthorizedAccessException
                            or ArgumentException
                            or NotSupportedException
                )
            {
                error = ex.Message;
                LogHelper.LogWarning($"Failed to start process '{info.FileName}': {ex.Message}");
                return false;
            }
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        /// <summary>
        /// Launches a file via Windows ShellExecute, bypassing Mono's Process.Start.
        /// Mono's Process.Start fails on built Windows apps (Win32Exception with NativeErrorCode=0)
        /// for explorer.exe and self-contained .NET PE binaries; ShellExecuteW handles both.
        /// Returns false (with a non-empty <paramref name="error"/>) when ShellExecute returns &lt;= 32.
        /// </summary>
        public static bool TryShellExecute(
            string file,
            string parameters,
            string workingDirectory,
            out string error
        )
        {
            error = null;
            IntPtr result = ShellExecuteW(
                IntPtr.Zero,
                "open",
                file,
                parameters,
                workingDirectory,
                SW_SHOWNORMAL
            );
            long code = result.ToInt64();
            if (code <= 32)
            {
                error = $"ShellExecute code {code}";
                LogHelper.LogWarning($"ShellExecute failed for '{file}': code {code}");
                return false;
            }
            return true;
        }

        static bool RevealOnWindows(string normalizedPath, bool isDirectory)
        {
            string file = isDirectory ? normalizedPath : "explorer.exe";
            string parameters = isDirectory ? null : $"/select,{QuoteArgument(normalizedPath)}";
            return TryShellExecute(file, parameters, null, out _);
        }
#endif

        /// <summary>
        /// Quotes a command-line argument for process launch arguments.
        /// </summary>
        /// <param name="value">The raw argument value.</param>
        /// <returns>The quoted argument value.</returns>
        public static string QuoteArgument(string value)
        {
            if (value is null)
                return "\"\"";

            return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        }

        #endregion

        #region Path

        /// <summary>
        /// Replaces characters that are invalid in a file name with a safe fallback.
        /// </summary>
        public static string SanitizeFileName(string source, char replacement = '_')
        {
            if (string.IsNullOrEmpty(source))
                return source;

            char[] invalidChars = Path.GetInvalidFileNameChars();
            char[] result = source.ToCharArray();
            for (int i = 0; i < result.Length; i++)
            {
                if (Array.IndexOf(invalidChars, result[i]) >= 0)
                    result[i] = replacement;
            }

            return new string(result);
        }

        #endregion

        #region Url

        /// <summary>
        /// Joins a base URL and relative path with a single slash.
        /// </summary>
        /// <param name="baseUrl">The base URL.</param>
        /// <param name="path">The relative URL path.</param>
        /// <returns>The combined URL.</returns>
        public static string JoinUrl(string baseUrl, string path)
        {
            if (string.IsNullOrEmpty(baseUrl))
                return path ?? string.Empty;

            if (string.IsNullOrEmpty(path))
                return baseUrl;

            return baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
        }

        #endregion

        #region Reflection

        public static string GetEnumName<T>(object key)
            where T : Enum
        {
            return Enum.GetName(
                typeof(T),
                Convert.ChangeType(key, Enum.GetUnderlyingType(typeof(T)))
            );
        }

        /// <summary>
        /// Gets the maximum value defined in an enum
        /// </summary>
        public static T GetEnumMax<T>()
            where T : Enum
        {
            return Enum.GetValues(typeof(T)).Cast<T>().Max();
        }

        /// <summary>
        /// Enum to Int with safely
        /// </summary>
        public static int EnumToInt<T>(T enumValue, int fallback = 0)
            where T : struct, Enum
        {
            try
            {
                return Convert.ToInt32(enumValue);
            }
            catch
            {
                return fallback;
            }
        }

        /// <summary>
        /// 检查Int或Enum类型数值
        /// </summary>
        public static bool CompareInt(int source, int expected, Comparison comparison) =>
            comparison switch
            {
                Comparison.Equal => source == expected,
                Comparison.Less => source < expected,
                Comparison.Greater => source > expected,
                Comparison.Equal | Comparison.Less => source <= expected,
                Comparison.Equal | Comparison.Greater => source >= expected,
                Comparison.Less | Comparison.Greater => source != expected,
                _ => false,
            };

        /// <summary>
        /// Get Type Name in Assembly-CSharp
        /// </summary>
        /// <param name="typeName"></param>
        /// <returns></returns>
        public static bool TryGetType(string typeName, out Type type)
        {
            type = Type.GetType($"{string.Join('.', NS_NAME, typeName)}, {DEF_ASSEMBLY_NAME}");
            return type != null;
        }

        /// <summary>
        /// Finds a loaded type by full name, searching all loaded assemblies when direct lookup fails.
        /// </summary>
        public static Type FindType(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return null;

            Type type = Type.GetType(fullName);
            if (type != null)
                return type;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(fullName);
                if (type != null)
                    return type;
            }

            return null;
        }

        /// <summary>
        /// Finds a loaded type by full name or throws when it is missing.
        /// </summary>
        public static Type RequireType(string fullName)
        {
            Type type = FindType(fullName);
            if (type == null)
                throw new TypeLoadException($"Type not found: {fullName}");

            return type;
        }

        /// <summary>
        /// Creates an instance or preserves the reflection exception for callers that must fail loudly.
        /// </summary>
        public static object CreateRequiredInstance(
            Type type,
            params object[] constructorParameters
        )
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            return Activator.CreateInstance(type, constructorParameters);
        }

        /// <summary>
        /// Finds an instance method by name and parameter count, walking base types.
        /// </summary>
        public static MethodInfo FindInstanceMethod(
            Type type,
            string methodName,
            int parameterCount,
            bool includeNonPublic = true
        )
        {
            if (type == null || string.IsNullOrEmpty(methodName) || parameterCount < 0)
                return null;

            BindingFlags flags = GetInstanceDeclaredFlags(includeNonPublic);
            for (Type current = type; current != null; current = current.BaseType)
            {
                foreach (var method in current.GetMethods(flags))
                {
                    if (
                        method.Name == methodName
                        && method.GetParameters().Length == parameterCount
                    )
                        return method;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds an instance method by name and parameter count or throws when it is missing.
        /// </summary>
        public static MethodInfo RequireInstanceMethod(
            Type type,
            string methodName,
            int parameterCount,
            bool includeNonPublic = true
        )
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            var method = FindInstanceMethod(type, methodName, parameterCount, includeNonPublic);
            if (method == null)
            {
                throw new MissingMethodException(
                    $"Method not found: {type.FullName}.{methodName} with {parameterCount} parameter(s)."
                );
            }

            return method;
        }

        /// <summary>
        /// Finds an exact static method overload, including non-public methods.
        /// </summary>
        public static MethodInfo FindStaticMethod(
            Type type,
            string methodName,
            params Type[] parameterTypes
        )
        {
            if (type == null || string.IsNullOrEmpty(methodName))
                return null;

            BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            return type.GetMethod(
                methodName,
                flags,
                binder: null,
                types: parameterTypes ?? Type.EmptyTypes,
                modifiers: null
            );
        }

        /// <summary>
        /// Finds an exact static method overload or throws when it is missing.
        /// </summary>
        public static MethodInfo RequireStaticMethod(
            Type type,
            string methodName,
            params Type[] parameterTypes
        )
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            MethodInfo method = FindStaticMethod(type, methodName, parameterTypes);
            if (method == null)
            {
                throw new MissingMethodException(
                    $"Static method not found: {type.FullName}.{methodName}."
                );
            }

            return method;
        }

        /// <summary>
        /// Finds an instance property by name, walking base types.
        /// </summary>
        public static PropertyInfo FindInstanceProperty(
            Type type,
            string propertyName,
            bool includeNonPublic = true
        )
        {
            if (type == null || string.IsNullOrEmpty(propertyName))
                return null;

            BindingFlags flags = GetInstanceDeclaredFlags(includeNonPublic);
            for (Type current = type; current != null; current = current.BaseType)
            {
                var property = current.GetProperty(propertyName, flags);
                if (property != null)
                    return property;
            }

            return null;
        }

        /// <summary>
        /// Finds an instance property by name or throws when it is missing.
        /// </summary>
        public static PropertyInfo RequireInstanceProperty(
            Type type,
            string propertyName,
            bool includeNonPublic = true
        )
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            var property = FindInstanceProperty(type, propertyName, includeNonPublic);
            if (property == null)
                throw new MissingMemberException(
                    $"Property not found: {type.FullName}.{propertyName}."
                );

            return property;
        }

        /// <summary>
        /// Finds an instance field by name, walking base types.
        /// </summary>
        public static FieldInfo FindInstanceField(
            Type type,
            string fieldName,
            bool includeNonPublic = true
        )
        {
            if (type == null || string.IsNullOrEmpty(fieldName))
                return null;

            BindingFlags flags = GetInstanceDeclaredFlags(includeNonPublic);
            for (Type current = type; current != null; current = current.BaseType)
            {
                var field = current.GetField(fieldName, flags);
                if (field != null)
                    return field;
            }

            return null;
        }

        /// <summary>
        /// Finds an instance field by name or throws when it is missing.
        /// </summary>
        public static FieldInfo RequireInstanceField(
            Type type,
            string fieldName,
            bool includeNonPublic = true
        )
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            var field = FindInstanceField(type, fieldName, includeNonPublic);
            if (field == null)
                throw new MissingFieldException($"Field not found: {type.FullName}.{fieldName}.");

            return field;
        }

        /// <summary>
        /// Reads an instance property or field value by name, or returns a fallback value when missing or mismatched.
        /// </summary>
        public static T GetInstanceMemberValueOrDefault<T>(
            object instance,
            string memberName,
            T fallback = default
        )
        {
            return TryGetInstanceMemberValue(instance, memberName, out T value) ? value : fallback;
        }

        /// <summary>
        /// Tries to read an instance property or field value by name, walking base types.
        /// </summary>
        public static bool TryGetInstanceMemberValue<T>(
            object instance,
            string memberName,
            out T value
        )
        {
            value = default;
            if (instance == null || string.IsNullOrEmpty(memberName))
                return false;

            Type type = instance.GetType();
            var property = FindInstanceProperty(type, memberName);
            if (property != null && property.CanRead)
            {
                object propertyValue = property.GetValue(instance);
                return TryAssignValue(propertyValue, out value);
            }

            var field = FindInstanceField(type, memberName);
            if (field == null)
                return false;

            object fieldValue = field.GetValue(instance);
            return TryAssignValue(fieldValue, out value);
        }

        /// <summary>
        /// Gets an instance field value using reflection
        /// </summary>
        /// <param name="instance">Instance object to get the field from</param>
        /// <param name="fieldName">Field name</param>
        /// <returns>Field value or null if not found</returns>
        public static object GetInstanceField(object instance, string fieldName)
        {
            try
            {
                if (instance == null)
                {
                    LogHelper.LogError("Instance is null");
                    return null;
                }

                Type type = instance.GetType();
                FieldInfo field = type.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );

                if (field == null)
                {
                    LogHelper.LogError(
                        $"Instance field '{fieldName}' not found in type '{type.Name}'"
                    );
                    return null;
                }

                return field.GetValue(instance);
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error getting instance field '{fieldName}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Sets an instance field value using reflection
        /// </summary>
        /// <param name="instance">Instance object to set the field on</param>
        /// <param name="fieldName">Field name</param>
        /// <param name="value">Value to set</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool SetInstanceField(object instance, string fieldName, object value)
        {
            try
            {
                if (instance == null)
                {
                    LogHelper.LogError("Instance is null");
                    return false;
                }

                Type type = instance.GetType();
                FieldInfo field = type.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );

                if (field == null)
                {
                    LogHelper.LogError(
                        $"Instance field '{fieldName}' not found in type '{type.Name}'"
                    );
                    return false;
                }

                field.SetValue(instance, value);
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error setting instance field '{fieldName}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets an instance property value using reflection
        /// </summary>
        /// <param name="instance">Instance object to get the property from</param>
        /// <param name="propertyName">Property name</param>
        /// <returns>Property value or null if not found</returns>
        public static object GetInstanceProperty(object instance, string propertyName)
        {
            try
            {
                if (instance == null)
                {
                    LogHelper.LogError("Instance is null");
                    return null;
                }

                Type type = instance.GetType();
                PropertyInfo property = type.GetProperty(
                    propertyName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );

                if (property == null)
                {
                    LogHelper.LogError(
                        $"Instance property '{propertyName}' not found in type '{type.Name}'"
                    );
                    return null;
                }

                if (!property.CanRead)
                {
                    LogHelper.LogError($"Instance property '{propertyName}' is not readable");
                    return null;
                }

                return property.GetValue(instance);
            }
            catch (Exception ex)
            {
                LogHelper.LogError(
                    $"Error getting instance property '{propertyName}': {ex.Message}"
                );
                return null;
            }
        }

        /// <summary>
        /// Sets an instance property value using reflection
        /// </summary>
        /// <param name="instance">Instance object to set the property on</param>
        /// <param name="propertyName">Property name</param>
        /// <param name="value">Value to set</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool SetInstanceProperty(object instance, string propertyName, object value)
        {
            try
            {
                if (instance == null)
                {
                    LogHelper.LogError("Instance is null");
                    return false;
                }

                Type type = instance.GetType();
                PropertyInfo property = type.GetProperty(
                    propertyName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );

                if (property == null)
                {
                    LogHelper.LogError(
                        $"Instance property '{propertyName}' not found in type '{type.Name}'"
                    );
                    return false;
                }

                if (!property.CanWrite)
                {
                    LogHelper.LogError($"Instance property '{propertyName}' is not writable");
                    return false;
                }

                property.SetValue(instance, value);
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.LogError(
                    $"Error setting instance property '{propertyName}': {ex.Message}"
                );
                return false;
            }
        }

        /// <summary>
        /// Calls an instance method from any assembly using reflection
        /// </summary>
        /// <param name="instance">Instance object to call the method on</param>
        /// <param name="methodName">Method name to call</param>
        /// <param name="parameters">Parameters to pass to the method</param>
        /// <returns>The return value of the method, or null if void or failed</returns>
        public static object CallInstanceMethod(
            object instance,
            string methodName,
            params object[] parameters
        )
        {
            try
            {
                if (instance == null)
                {
                    LogHelper.LogError("Instance is null");
                    return null;
                }

                Type type = instance.GetType();
                MethodInfo method = GetMethod(type, methodName, parameters, isStatic: false);

                if (method == null)
                {
                    LogHelper.LogError(
                        $"Instance method '{methodName}' not found in type '{type.Name}'"
                    );
                    return null;
                }

                return method.Invoke(instance, parameters);
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error calling instance method '{methodName}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Calls a static method from any assembly using reflection
        /// </summary>
        /// <param name="typeName">Full type name (e.g., "Hypocycloid.AIP.Cmd")</param>
        /// <param name="methodName">Method name to call</param>
        /// <param name="parameters">Parameters to pass to the method (null if no parameters)</param>
        /// <param name="assemblyName">Assembly name (optional, will try default first)</param>
        /// <returns>The return value of the method, or null if void or failed</returns>
        public static object CallStaticMethod(
            string typeName,
            string methodName,
            object[] parameters = null,
            string assemblyName = DEF_ASSEMBLY_NAME
        )
        {
            try
            {
                Type type = GetType(typeName, assemblyName);
                if (type == null)
                {
                    LogHelper.LogError($"Type '{typeName}' not found in assembly");
                    return null;
                }

                MethodInfo method = GetMethod(type, methodName, parameters);
                if (method == null)
                {
                    LogHelper.LogError(
                        $"Static method '{methodName}' not found in type '{typeName}'"
                    );
                    return null;
                }

                return method.Invoke(null, parameters);
            }
            catch (Exception ex)
            {
                LogHelper.LogError(
                    $"Error calling static method '{methodName}' on '{typeName}': {ex.Message}"
                );
                return null;
            }
        }

        /// <summary>
        /// Gets a static field value from any assembly using reflection
        /// </summary>
        /// <param name="typeName">Full type name</param>
        /// <param name="fieldName">Field name</param>
        /// <param name="assemblyName">Assembly name (optional)</param>
        /// <returns>Field value or null if not found</returns>
        public static object GetStaticField(
            string typeName,
            string fieldName,
            string assemblyName = DEF_ASSEMBLY_NAME
        )
        {
            try
            {
                Type type = GetType(typeName, assemblyName);
                if (type == null)
                    return null;

                FieldInfo field = type.GetField(
                    fieldName,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                );
                if (field == null)
                {
                    LogHelper.LogError(
                        $"Static field '{fieldName}' not found in type '{typeName}'"
                    );
                    return null;
                }

                return field.GetValue(null);
            }
            catch (Exception ex)
            {
                LogHelper.LogError(
                    $"Error getting static field '{fieldName}' from '{typeName}': {ex.Message}"
                );
                return null;
            }
        }

        /// <summary>
        /// Sets a static field value from any assembly using reflection
        /// </summary>
        /// <param name="typeName">Full type name</param>
        /// <param name="fieldName">Field name</param>
        /// <param name="value">Value to set</param>
        /// <param name="assemblyName">Assembly name (optional)</param>
        public static bool SetStaticField(
            string typeName,
            string fieldName,
            object value,
            string assemblyName = DEF_ASSEMBLY_NAME
        )
        {
            try
            {
                Type type = GetType(typeName, assemblyName);
                if (type == null)
                    return false;

                FieldInfo field = type.GetField(
                    fieldName,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                );
                if (field == null)
                {
                    LogHelper.LogError(
                        $"Static field '{fieldName}' not found in type '{typeName}'"
                    );
                    return false;
                }

                field.SetValue(null, value);
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.LogError(
                    $"Error setting static field '{fieldName}' in '{typeName}': {ex.Message}"
                );
                return false;
            }
        }

        /// <summary>
        /// Creates an instance of a type from any assembly
        /// </summary>
        /// <param name="typeName">Full type name</param>
        /// <param name="constructorParams">Constructor parameters</param>
        /// <param name="assemblyName">Assembly name (optional)</param>
        /// <returns>Instance of the type or null if failed</returns>
        public static object CreateInstance(
            string typeName,
            object[] constructorParams = null,
            string assemblyName = DEF_ASSEMBLY_NAME
        )
        {
            try
            {
                Type type = GetType(typeName, assemblyName);
                if (type == null)
                    return null;

                return Activator.CreateInstance(type, constructorParams);
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error creating instance of '{typeName}': {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Internal Helpers

        static Type GetType(string typeName, string assemblyName = null)
        {
            Type type = null;

            // Try without assembly name first
            type = Type.GetType(typeName);

            // If not found and assembly name provided, try with assembly
            if (type == null && !string.IsNullOrEmpty(assemblyName))
            {
                type = Type.GetType($"{typeName}, {assemblyName}");
            }

            // If still not found, try common Unity assemblies
            if (type == null)
            {
                string[] commonAssemblies = { DEF_ASSEMBLY_NAME, $"{DEF_ASSEMBLY_NAME}-firstpass" };

                foreach (string assembly in commonAssemblies)
                {
                    type = Type.GetType($"{typeName}, {assembly}");
                    if (type != null)
                        break;
                }
            }

            return type;
        }

        static MethodInfo GetMethod(
            Type type,
            string methodName,
            object[] parameters,
            bool isStatic = true
        )
        {
            BindingFlags flags = isStatic
                ? BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                : BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            if (parameters == null || parameters.Length == 0)
            {
                return type.GetMethod(methodName, flags);
            }

            // Get parameter types
            Type[] paramTypes = new Type[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                paramTypes[i] = parameters[i]?.GetType() ?? typeof(object);
            }

            return type.GetMethod(methodName, flags, null, paramTypes, null);
        }

        static BindingFlags GetInstanceDeclaredFlags(bool includeNonPublic)
        {
            BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
            if (includeNonPublic)
                flags |= BindingFlags.NonPublic;
            return flags;
        }

        static bool TryAssignValue<T>(object rawValue, out T value)
        {
            value = default;
            if (rawValue == null)
                return !typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null;

            if (rawValue is not T typedValue)
                return false;

            value = typedValue;
            return true;
        }

        #endregion

        #region Binding Class

        public class Binding
        {
            public string DisplayName;
            public Type DataType;
            public object Instance;
            public bool Reactive;
            public int Priority;

            public MethodInfo Method { get; private set; }
            public bool IsMethod => Method != null;

            FieldInfo _field;
            PropertyInfo _property;

            public Binding(
                string name,
                object instance,
                FieldInfo field,
                bool reactive = false,
                int priority = 0
            )
            {
                DisplayName = name;
                Instance = instance;
                _field = field;
                DataType = field.FieldType;
                Reactive = reactive;
                Priority = priority;
            }

            public Binding(
                string name,
                object instance,
                PropertyInfo property,
                bool reactive = false,
                int priority = 0
            )
            {
                DisplayName = name;
                Instance = instance;
                _property = property;
                DataType = property.PropertyType;
                Reactive = reactive;
                Priority = priority;
            }

            public Binding(
                string name,
                object instance,
                MethodInfo method,
                bool reactive = false,
                int priority = 0
            )
            {
                DisplayName = name;
                Instance = instance;
                Method = method;
                DataType = typeof(void); // Methods don't need a value type for UI rendering
                Reactive = reactive;
                Priority = priority;
            }

            public object GetValue()
            {
                return _field != null ? _field.GetValue(Instance) : _property.GetValue(Instance);
            }

            public void SetValue(object value)
            {
                try
                {
                    // Handles converting strings from InputFields back to ints/floats
                    object convertedValue = Convert.ChangeType(value, DataType);

                    if (_field != null)
                        _field.SetValue(Instance, convertedValue);
                    else if (_property != null && _property.CanWrite)
                        _property.SetValue(Instance, convertedValue);
                }
                catch (Exception e)
                {
                    LogHelper.LogError(
                        $"[System] Failed to set value for {DisplayName}: {e.Message}"
                    );
                }
            }

            // Execute the mapped method
            public void InvokeMethod()
            {
                if (IsMethod)
                {
                    Method.Invoke(Instance, null);
                }
            }
        }

        #endregion
    }
}
