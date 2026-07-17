using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Hypocycloid.Utils;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Hypocycloid.Editor
{
    static class TestHelper
    {
        public static MethodInfo GetPrivateInstanceMethod<T>(
            string methodName,
            int parameterCount = 0
        )
        {
            return SystemHelper.RequireInstanceMethod(
                typeof(T),
                methodName,
                parameterCount,
                includeNonPublic: true
            );
        }

        public static FieldInfo GetPrivateInstanceField<T>(string fieldName)
        {
            return SystemHelper.RequireInstanceField(typeof(T), fieldName, includeNonPublic: true);
        }

        public static FieldInfo GetPrivateInstanceField(Type type, string fieldName)
        {
            return SystemHelper.RequireInstanceField(type, fieldName, includeNonPublic: true);
        }

        public static IEnumerator RunInPlayMode(Func<IEnumerator> createRoutine)
        {
            Exception failure = null;
            IEnumerator routine = null;

            yield return new EnterPlayMode();

            try
            {
                routine = createRoutine();
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            while (failure == null && routine != null)
            {
                object current = null;
                bool moved = false;
                try
                {
                    moved = routine.MoveNext();
                    if (moved)
                        current = routine.Current;
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                if (failure != null || !moved)
                    break;

                yield return current;
            }

            yield return new ExitPlayMode();

            if (failure != null)
                throw failure;
        }

        public static IEnumerator RunEnumerator(
            IEnumerator routine,
            string label,
            float timeoutSeconds
        )
        {
            Assert.That(routine, Is.Not.Null, $"{label} routine was not found.");

            double deadline = EditorApplication.timeSinceStartup + timeoutSeconds;
            Stack<IEnumerator> stack = new();
            stack.Push(routine);

            while (stack.Count > 0)
            {
                if (EditorApplication.timeSinceStartup > deadline)
                    Assert.Fail($"{label} timed out after {timeoutSeconds:0} seconds.");

                IEnumerator current = stack.Peek();
                bool moved;
                try
                {
                    moved = current.MoveNext();
                }
                catch (Exception ex)
                {
                    Assert.Fail($"{label} threw: {ex}");
                    yield break;
                }

                if (!moved)
                {
                    stack.Pop();
                    continue;
                }

                if (current.Current is IEnumerator nested)
                {
                    stack.Push(nested);
                    continue;
                }

                yield return current.Current;
            }
        }

        public static void AssertPng(string path, int expectedWidth, int expectedHeight)
        {
            Assert.That(path, Is.Not.Null.And.Not.Empty);
            Assert.That(File.Exists(path), Is.True, path);
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(0), path);

            byte[] bytes = File.ReadAllBytes(path);
            Texture2D texture = new(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(texture.LoadImage(bytes), Is.True, $"PNG did not decode: {path}");
                Assert.That(texture.width, Is.EqualTo(expectedWidth));
                Assert.That(texture.height, Is.EqualTo(expectedHeight));
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
