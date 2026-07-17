using UnityEngine;

namespace Hypocycloid.Utils
{
    [DisallowMultipleComponent]
    public class PositionTrailer : MonoBehaviour
    {
        const int REFERENCE_FRAMERATE = 60;
        const float POSITION_EPSILON = 0.000001f;

        public enum PositionSpace
        {
            Auto,
            AnchoredPosition,
            LocalPosition,
            WorldPosition,
            RectTransformBorder,
        }

        enum RectBorderSide
        {
            Left,
            Right,
            Bottom,
            Top,
        }

        enum RectBorderTangentAnchor
        {
            Min,
            Center,
            Max,
        }

        public Transform leader;
        public float followSharpness = 0.1f;
        public bool isSmoothUpdate;
        public PositionSpace positionSpace;

        Transform _self;
        RectTransform _selfRect;
        RectTransform _leaderRect;
        Transform _cachedLeader;
        Transform _cachedSelfParent;
        Transform _cachedLeaderParent;
        PositionSpace _cachedPositionSpace;
        Vector3 _worldOffset;
        Vector3 _localOffset;
        Vector2 _anchoredOffset;
        RectBorderSide _leaderRectBorderSide;
        RectBorderTangentAnchor _leaderRectBorderTangentAnchor;
        float _leaderRectBorderTangentOffset;
        float _leaderRectBorderNormalOffset;
        bool _useAnchoredRectBorder;
        PositionSpace _resolvedPositionSpace;
        bool _offsetReady;

        void Awake()
        {
            CacheTransforms();
            CacheOffset();
        }

        void Start()
        {
            if (!_offsetReady)
                CacheOffset();
        }

        void LateUpdate()
        {
            DoFollow();
        }

        public void Configure(Transform followLeader, bool recalculateOffset = false)
        {
            CacheTransforms();

            if (leader != followLeader)
            {
                leader = followLeader;
                _offsetReady = false;
            }

            if (recalculateOffset || !_offsetReady)
                CacheOffset();
        }

        public void CacheOffset()
        {
            CacheTransforms();
            if (leader == null)
                return;

            _leaderRect = leader as RectTransform;
            _resolvedPositionSpace = ResolvePositionSpace();

            switch (_resolvedPositionSpace)
            {
                case PositionSpace.RectTransformBorder:
                    CacheRectTransformBorderOffset();
                    break;
                case PositionSpace.AnchoredPosition:
                    _anchoredOffset = _selfRect.anchoredPosition - _leaderRect.anchoredPosition;
                    break;
                case PositionSpace.LocalPosition:
                    _localOffset = _self.localPosition - leader.localPosition;
                    break;
                default:
                    _worldOffset = _self.position - leader.position;
                    break;
            }

            _cachedLeader = leader;
            _cachedSelfParent = _self.parent;
            _cachedLeaderParent = leader.parent;
            _cachedPositionSpace = positionSpace;
            _offsetReady = true;
        }

        public void DoFollow()
        {
            if (leader == null)
                return;

            if (!_offsetReady || IsOffsetCacheStale())
                CacheOffset();
            if (!_offsetReady)
                return;

            float sharpness = GetAdjustedSharpness();
            if (sharpness <= 0f)
                return;

            switch (_resolvedPositionSpace)
            {
                case PositionSpace.RectTransformBorder:
                    FollowRectTransformBorder(sharpness);
                    break;
                case PositionSpace.AnchoredPosition:
                    FollowAnchored(sharpness);
                    break;
                case PositionSpace.LocalPosition:
                    FollowLocal(sharpness);
                    break;
                default:
                    FollowWorld(sharpness);
                    break;
            }
        }

        void CacheTransforms()
        {
            if (_self == null)
                _self = transform;
            if (_selfRect == null)
                _selfRect = _self as RectTransform;
            _leaderRect = leader as RectTransform;
        }

        bool IsOffsetCacheStale()
        {
            return _cachedLeader != leader
                || _cachedSelfParent != _self.parent
                || _cachedLeaderParent != leader.parent
                || _cachedPositionSpace != positionSpace;
        }

        PositionSpace ResolvePositionSpace()
        {
            if (positionSpace == PositionSpace.RectTransformBorder && CanUseRectTransformBorder())
                return PositionSpace.RectTransformBorder;
            if (positionSpace == PositionSpace.AnchoredPosition && CanUseAnchoredPosition())
                return PositionSpace.AnchoredPosition;
            if (positionSpace == PositionSpace.LocalPosition && CanUseLocalPosition())
                return PositionSpace.LocalPosition;
            if (positionSpace == PositionSpace.WorldPosition)
                return PositionSpace.WorldPosition;

            if (CanUseAnchoredPosition())
                return PositionSpace.AnchoredPosition;
            if (CanUseLocalPosition())
                return PositionSpace.LocalPosition;
            return PositionSpace.WorldPosition;
        }

        bool CanUseAnchoredPosition()
        {
            return _selfRect != null
                && _leaderRect != null
                && _selfRect.parent == _leaderRect.parent;
        }

        bool CanUseLocalPosition()
        {
            return _self.parent == leader.parent;
        }

        bool CanUseRectTransformBorder()
        {
            return _leaderRect != null;
        }

        float GetAdjustedSharpness()
        {
            float sharpness = Mathf.Clamp01(followSharpness);
            if (sharpness >= 1f || !isSmoothUpdate)
                return sharpness;

            return 1f - Mathf.Pow(1f - sharpness, Time.deltaTime * REFERENCE_FRAMERATE);
        }

        void CacheRectTransformBorderOffset()
        {
            _useAnchoredRectBorder = CanUseAnchoredPosition();
            Rect rect = _leaderRect.rect;

            Vector2 leaderLocalPosition = _leaderRect.InverseTransformPoint(_self.position);
            _leaderRectBorderSide = SelectRectBorderSide(rect, leaderLocalPosition);
            float tangent = GetRectBorderTangent(_leaderRectBorderSide, leaderLocalPosition);
            _leaderRectBorderTangentAnchor = SelectRectBorderTangentAnchor(
                rect,
                _leaderRectBorderSide,
                tangent
            );
            _leaderRectBorderTangentOffset =
                tangent
                - GetRectBorderTangentAnchorValue(
                    rect,
                    _leaderRectBorderSide,
                    _leaderRectBorderTangentAnchor
                );
            _leaderRectBorderNormalOffset = GetRectBorderNormalOffset(
                _leaderRectBorderSide,
                rect,
                leaderLocalPosition
            );
        }

        void FollowRectTransformBorder(float sharpness)
        {
            Rect rect = _leaderRect.rect;
            Vector2 targetLocalPosition = RectBorderTargetPoint(rect);

            if (
                _useAnchoredRectBorder
                && IsSameLocalRotation(_leaderRect, Quaternion.identity)
                && IsSameLocalScale(_leaderRect, Vector3.one)
            )
            {
                Vector2 anchoredTargetPosition = _leaderRect.anchoredPosition + targetLocalPosition;
                FollowAnchoredPosition(anchoredTargetPosition, sharpness);
                return;
            }

            Vector3 targetPosition = _leaderRect.TransformPoint(targetLocalPosition);
            Vector3 currentPosition = _self.position;
            Vector3 delta = targetPosition - currentPosition;
            if (delta.sqrMagnitude <= POSITION_EPSILON)
                return;

            if (sharpness >= 1f)
            {
                _self.position = targetPosition;
                return;
            }

            _self.position = currentPosition + delta * sharpness;
        }

        Vector2 RectBorderTargetPoint(Rect rect)
        {
            float tangent =
                GetRectBorderTangentAnchorValue(
                    rect,
                    _leaderRectBorderSide,
                    _leaderRectBorderTangentAnchor
                ) + _leaderRectBorderTangentOffset;
            return _leaderRectBorderSide switch
            {
                RectBorderSide.Left => new Vector2(
                    rect.xMin + _leaderRectBorderNormalOffset,
                    tangent
                ),
                RectBorderSide.Right => new Vector2(
                    rect.xMax + _leaderRectBorderNormalOffset,
                    tangent
                ),
                RectBorderSide.Bottom => new Vector2(
                    tangent,
                    rect.yMin + _leaderRectBorderNormalOffset
                ),
                _ => new Vector2(tangent, rect.yMax + _leaderRectBorderNormalOffset),
            };
        }

        static RectBorderSide SelectRectBorderSide(Rect rect, Vector2 point)
        {
            float leftOutside = rect.xMin - point.x;
            float rightOutside = point.x - rect.xMax;
            float bottomOutside = rect.yMin - point.y;
            float topOutside = point.y - rect.yMax;
            float horizontalOutside = Mathf.Max(leftOutside, rightOutside, 0f);
            float verticalOutside = Mathf.Max(bottomOutside, topOutside, 0f);

            if (horizontalOutside > 0f || verticalOutside > 0f)
            {
                if (verticalOutside >= horizontalOutside)
                    return bottomOutside > topOutside ? RectBorderSide.Bottom : RectBorderSide.Top;
                return leftOutside > rightOutside ? RectBorderSide.Left : RectBorderSide.Right;
            }

            return ClosestRectBorderSide(rect, point);
        }

        static RectBorderSide ClosestRectBorderSide(Rect rect, Vector2 point)
        {
            float left = Mathf.Abs(point.x - rect.xMin);
            float right = Mathf.Abs(rect.xMax - point.x);
            float bottom = Mathf.Abs(point.y - rect.yMin);
            float top = Mathf.Abs(rect.yMax - point.y);
            float nearest = Mathf.Min(Mathf.Min(left, right), Mathf.Min(bottom, top));

            if (nearest == left)
                return RectBorderSide.Left;
            if (nearest == right)
                return RectBorderSide.Right;
            if (nearest == bottom)
                return RectBorderSide.Bottom;
            return RectBorderSide.Top;
        }

        static RectBorderTangentAnchor SelectRectBorderTangentAnchor(
            Rect rect,
            RectBorderSide side,
            float tangent
        )
        {
            float min = GetRectBorderTangentAnchorValue(rect, side, RectBorderTangentAnchor.Min);
            float center = GetRectBorderTangentAnchorValue(
                rect,
                side,
                RectBorderTangentAnchor.Center
            );
            float max = GetRectBorderTangentAnchorValue(rect, side, RectBorderTangentAnchor.Max);
            float minDistance = Mathf.Abs(tangent - min);
            float centerDistance = Mathf.Abs(tangent - center);
            float maxDistance = Mathf.Abs(tangent - max);

            if (centerDistance <= minDistance && centerDistance <= maxDistance)
                return RectBorderTangentAnchor.Center;
            return minDistance <= maxDistance
                ? RectBorderTangentAnchor.Min
                : RectBorderTangentAnchor.Max;
        }

        static float GetRectBorderTangentAnchorValue(
            Rect rect,
            RectBorderSide side,
            RectBorderTangentAnchor anchor
        )
        {
            bool verticalSide = side == RectBorderSide.Left || side == RectBorderSide.Right;
            return anchor switch
            {
                RectBorderTangentAnchor.Min => verticalSide ? rect.yMin : rect.xMin,
                RectBorderTangentAnchor.Max => verticalSide ? rect.yMax : rect.xMax,
                _ => verticalSide ? rect.center.y : rect.center.x,
            };
        }

        static float GetRectBorderNormalOffset(RectBorderSide side, Rect rect, Vector2 point)
        {
            return side switch
            {
                RectBorderSide.Left => point.x - rect.xMin,
                RectBorderSide.Right => point.x - rect.xMax,
                RectBorderSide.Bottom => point.y - rect.yMin,
                _ => point.y - rect.yMax,
            };
        }

        static float GetRectBorderTangent(RectBorderSide side, Vector2 point)
        {
            return side == RectBorderSide.Left || side == RectBorderSide.Right ? point.y : point.x;
        }

        static bool IsSameLocalRotation(Transform target, Quaternion value)
        {
            return Quaternion.Angle(target.localRotation, value) <= 0.001f;
        }

        static bool IsSameLocalScale(Transform target, Vector3 value)
        {
            return (target.localScale - value).sqrMagnitude <= POSITION_EPSILON;
        }

        void FollowAnchored(float sharpness)
        {
            Vector2 targetPosition = _leaderRect.anchoredPosition + _anchoredOffset;
            FollowAnchoredPosition(targetPosition, sharpness);
        }

        void FollowAnchoredPosition(Vector2 targetPosition, float sharpness)
        {
            Vector2 currentPosition = _selfRect.anchoredPosition;
            Vector2 delta = targetPosition - currentPosition;
            if (delta.sqrMagnitude <= POSITION_EPSILON)
                return;

            _selfRect.anchoredPosition =
                sharpness >= 1f ? targetPosition : currentPosition + delta * sharpness;
        }

        void FollowLocal(float sharpness)
        {
            Vector3 targetPosition = leader.localPosition + _localOffset;
            Vector3 currentPosition = _self.localPosition;
            Vector3 delta = targetPosition - currentPosition;
            if (delta.sqrMagnitude <= POSITION_EPSILON)
                return;

            _self.localPosition =
                sharpness >= 1f ? targetPosition : currentPosition + delta * sharpness;
        }

        void FollowWorld(float sharpness)
        {
            Vector3 targetPosition = leader.position + _worldOffset;
            Vector3 currentPosition = _self.position;
            Vector3 delta = targetPosition - currentPosition;
            if (delta.sqrMagnitude <= POSITION_EPSILON)
                return;

            if (sharpness >= 1f)
            {
                _self.position = targetPosition;
                return;
            }

            _self.position = currentPosition + delta * sharpness;
        }
    }
}
