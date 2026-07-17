using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Hypocycloid.Utils
{
    public class VectorHelper
    {
        public static Vector3 GetClosestPointAmongst(
            Renderer[] renderers,
            Vector3 worldPos,
            out Renderer closestRenderer
        )
        {
            Vector3 closestPoint = Vector3.positiveInfinity;
            float minDistance = float.MaxValue;
            closestRenderer = null;

            foreach (var renderer in renderers)
            {
                if (renderer == null)
                    continue;

                Vector3 point = renderer.bounds.ClosestPoint(worldPos);
                float distance = Vector3.SqrMagnitude(worldPos - point); // Using SqrMagnitude for performance

                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestPoint = point;
                    closestRenderer = renderer;
                }
            }

            return closestPoint;
        }

        public static Vector2 GetRandomPointOn2DBound(Bounds bounds, float variance = 0, int edgeIndex = -1)
        {
            // Choose a random face of the bounding box
            edgeIndex = edgeIndex < 0 ? Random.Range(0, 4) : edgeIndex;
            float randomDistance = Random.Range(0, variance);

            // Generate a random point on the chosen face
            return edgeIndex switch
            {
                // Top edge
                0 => new(Random.Range(bounds.min.x, bounds.max.x), bounds.max.y - randomDistance),
                // Right edge
                1 => new(bounds.max.x - randomDistance, Random.Range(bounds.min.y, bounds.max.y)),
                // Bottom edge
                2 => new(Random.Range(bounds.min.x, bounds.max.x), bounds.min.y + randomDistance),
                // Left edge
                3 => new(bounds.min.x + randomDistance, Random.Range(bounds.min.y, bounds.max.y)),
                _ => Vector2.zero, // Should not happen
            };
        }

        public static Vector3 GetRandomPointOn3DBound(Bounds bounds, float variance = 0, int edgeIndex = -1)
        {
            // Choose a random face of the bounding box
            edgeIndex = edgeIndex < 0 ? Random.Range(0, 4) : edgeIndex;
            float randomDistance = Random.Range(0, variance);

            // Generate a random point on the chosen face
            return edgeIndex switch
            {
                // Front face
                0 => new(
                    Random.Range(bounds.min.x, bounds.max.x),
                    Random.Range(bounds.min.y, bounds.max.y),
                    bounds.min.z - randomDistance
                ),
                // Back face
                1 => new(
                    Random.Range(bounds.min.x, bounds.max.x),
                    Random.Range(bounds.min.y, bounds.max.y),
                    bounds.max.z + randomDistance
                ),
                // Left face
                2 => new(
                    bounds.min.x + randomDistance,
                    Random.Range(bounds.min.y, bounds.max.y),
                    Random.Range(bounds.min.z, bounds.max.z)
                ),
                // Right face
                3 => new(
                    bounds.max.x - randomDistance,
                    Random.Range(bounds.min.y, bounds.max.y),
                    Random.Range(bounds.min.z, bounds.max.z)
                ),
                // Top face
                4 => new(
                    Random.Range(bounds.min.x, bounds.max.x),
                    bounds.max.y - randomDistance,
                    Random.Range(bounds.min.z, bounds.max.z)
                ),
                // Bottom face
                5 => new(
                    Random.Range(bounds.min.x, bounds.max.x),
                    bounds.min.y + randomDistance,
                    Random.Range(bounds.min.z, bounds.max.z)
                ),
                _ => Vector3.zero, // Should not happen
            };
        }

        public static int CheckDirection(Vector3 fromPoint, Vector3 toPoint)
        {
            Vector3 direction = (toPoint - fromPoint).normalized;

            // Check if the direction is primarily horizontal or vertical
            float horizontalDot = Vector3.Dot(direction, Vector3.right);
            float verticalDot = Vector3.Dot(direction, Vector3.up);

            if (Mathf.Abs(horizontalDot) > Mathf.Abs(verticalDot))
            {
                // Horizontal direction
                if (horizontalDot > 0)
                    return 3;
                else
                    return 2;
            }
            else
            {
                // Vertical direction
                if (verticalDot > 0)
                    return 0;
                else
                    return 1;
            }
        }

        /// <summary>
        /// CUSTOM MATH (Fixes the "Look Rotation Viewing Vector is Zero" Hang)
        /// https://discussions.unity.com/t/how-todo-fully-automated-mesh-uv-unwrapping/779870/7
        /// </summary>
        public static Quaternion FromToRotation(Vector3 fromDir, Vector3 toDir, Quaternion ifOpposite)
        {
            float w = 1f + Vector3.Dot(fromDir, toDir);

            if (w < 1E-6f)
            {
                if (ifOpposite == default)
                    return Quaternion.FromToRotation(fromDir, toDir);
                return ifOpposite;
            }

            Vector3 xyz = Vector3.Cross(fromDir, toDir);
            return new Quaternion(xyz.x, xyz.y, xyz.z, w).normalized;
        }

        /// <summary>
        /// CUSTOM MATH (Fixes the "Look Rotation Viewing Vector is Zero" Hang)
        /// This calculates the rotation needed to move 'fromDir' to 'toDir' using the Half-Way quaternion method.
        /// It is robust against zero vectors (returns Identity) and safer than LookRotation.
        /// </summary>
        public static Quaternion FromToRotation(Vector3 fromDir, Vector3 toDir)
        {
            // Note: We assume input vectors are NOT normalized for safety, so we handle dot carefully
            float dot = Vector3.Dot(fromDir, toDir);
            float k = Mathf.Sqrt(fromDir.sqrMagnitude * toDir.sqrMagnitude);

            // "w" component of the quaternion (related to cos(theta/2))
            float w = k + dot;

            // Handle the 180 degree case (vectors opposite)
            if (w < MathUtils.EPS * k)
            {
                // Fallback: Use an arbitrary orthogonal axis
                // Unity's internal implementation handles this, but here is a math-only fallback:
                Vector3 axis = Vector3.Cross(Vector3.up, fromDir);
                if (axis.sqrMagnitude < MathUtils.EPS) // If fromDir was parallel to Up
                    axis = Vector3.Cross(Vector3.right, fromDir);

                return new Quaternion(axis.x, axis.y, axis.z, 0).normalized;
            }

            // Standard case
            Vector3 cross = Vector3.Cross(fromDir, toDir);
            return new Quaternion(cross.x, cross.y, cross.z, w).normalized;
        }
    }
}
