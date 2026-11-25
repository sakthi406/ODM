using System;
using System.Reflection;
using UnityEngine;

namespace ODM.Editor
{
    /// <summary>Type names ODM looks for in the running app. If the app renames one, change it here.</summary>
    internal static class AppTypes
    {
        public const string Flow = "SOPFlowController";
        public const string QrSimulator = "EditorQRSimulator";
        public const string UiManager = "UIManager";
        public const string QrBackend = "QRBackendController";
    }

    /// <summary>Late-bound access to the app's objects, so ODM never references the app's assemblies.</summary>
    internal static class Reflect
    {
        private const BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        public static MonoBehaviour FindSceneComponent(string typeName)
        {
            MonoBehaviour[] components = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
            MonoBehaviour inactiveMatch = null;
            foreach (MonoBehaviour component in components)
            {
                if (
                    component != null
                    && component.GetType().Name == typeName
                    && component.gameObject.scene.IsValid()
                    && component.gameObject.scene.isLoaded
                )
                {
                    if (component.isActiveAndEnabled)
                        return component;
                    if (inactiveMatch == null)
                        inactiveMatch = component;
                }
            }
            return inactiveMatch;
        }

        public static object Read(object target, string name)
        {
            if (target == null)
                return null;

            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, Flags);
                if (field != null)
                    return field.GetValue(target);

                PropertyInfo property = type.GetProperty(name, Flags);
                if (property != null)
                    return property.GetValue(target);
            }
            return null;
        }

        public static bool Write(object target, string name, object value)
        {
            if (target == null)
                return false;

            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, Flags);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return true;
                }
            }
            return false;
        }

        public static object Call(object target, string name, params object[] args)
        {
            if (target == null)
                throw new InvalidOperationException($"Cannot call {name}: target is missing.");

            MethodInfo method = FindMethod(target.GetType(), name, args);
            if (method == null)
                throw new MissingMethodException(target.GetType().Name, name);

            return method.Invoke(target, args);
        }

        public static MethodInfo FindMethod(Type type, string name, params object[] args)
        {
            for (; type != null; type = type.BaseType)
            {
                foreach (MethodInfo method in type.GetMethods(Flags))
                {
                    if (method.Name != name)
                        continue;

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != args.Length)
                        continue;

                    bool matches = true;
                    for (int i = 0; i < parameters.Length && matches; i++)
                    {
                        Type parameterType = parameters[i].ParameterType;
                        if (args[i] == null)
                            matches = !parameterType.IsValueType || Nullable.GetUnderlyingType(parameterType) != null;
                        else
                            matches = parameterType.IsInstanceOfType(args[i]);
                    }

                    if (matches)
                        return method;
                }
            }
            return null;
        }

        public static string Str(object target, string member)
        {
            object value = Read(target, member);
            return value == null ? string.Empty : value.ToString();
        }

        public static int Int(object target, string member)
        {
            return Read(target, member) is int value ? value : 0;
        }

        public static bool Bool(object target, string member)
        {
            return Read(target, member) is bool value && value;
        }

        /// <summary>Sets a writable string property named "text" (UnityEngine.UI.InputField, TMP_InputField, ...).</summary>
        public static bool SetText(object textObject, string text)
        {
            if (textObject == null)
                return false;

            PropertyInfo property = textObject.GetType().GetProperty("text");
            if (property == null || !property.CanWrite || property.PropertyType != typeof(string))
                return false;

            property.SetValue(textObject, text);
            return true;
        }
    }
}
