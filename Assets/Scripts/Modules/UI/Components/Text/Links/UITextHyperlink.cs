using System;
using Hypocycloid.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Hypocycloid.UI
{
    /// <summary>
    /// Allows links in TextMeshPro text objects to be clicked on, and gives them custom colors when they are hovered or clicked.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public class UITextHyperlink
        : MonoBehaviour,
            IPointerClickHandler,
            IPointerDownHandler,
            IPointerUpHandler,
            IPointerEnterHandler,
            IPointerExitHandler
    {
        TMP_Text text;
        public TMP_Text Text
        {
            get
            {
                if (text == null)
                    text = GetComponent<TMP_Text>();

                return text;
            }
        }

        void OnValidate()
        {
            SetAllLinksToNormalColor();
        }

        void OnEnable()
        {
            previouslyHoveredLinkIndex = -1;

            // I'm not sure why the frame delay is necessary, but it is.
            // I suspect that TMP changes the colors in LateUpdate or something
            CoroutineHelper.EndOfFrame(this, SetAllLinksToNormalColor);
        }

        void OnDisable()
        {
            HoverEnded();
        }

        [ColorUsage(showAlpha: false), SerializeField]
        Color32 LinkNormalColor = Color.yellow;

        [ColorUsage(showAlpha: false), SerializeField]
        Color32 LinkHoveredColor = Color.green;

        [ColorUsage(showAlpha: false), SerializeField]
        Color32 LinkClickColor = Color.red;

        public void SetColors(
            Color32 linkNormalColor,
            Color32 linkHoveredColor,
            Color32 linkClickColor
        )
        {
            LinkNormalColor = linkNormalColor;
            LinkHoveredColor = linkHoveredColor;
            LinkClickColor = linkClickColor;

            SetAllLinksToNormalColor();
        }

        public void LinkDataUpdated()
        {
            previouslyHoveredLinkIndex = -1;
            SetAllLinksToNormalColor();
        }

        public event Action<string> OnLinkClicked;

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            int linkIndex = TMP_TextUtilities.FindIntersectingLink(
                Text,
                eventData.pressPosition,
                eventData.pressEventCamera
            );
            if (linkIndex > -1)
            {
                var link = Text.textInfo.linkInfo[linkIndex];
                OnLinkClicked?.Invoke(link.GetLinkID());
            }
        }

        void IPointerDownHandler.OnPointerDown(PointerEventData eventData)
        {
            PointerIsDown = true;
            SetLinkToColor(previouslyHoveredLinkIndex, LinkClickColor);
        }

        void IPointerUpHandler.OnPointerUp(PointerEventData eventData)
        {
            PointerIsDown = false;
            SetLinkToColor(previouslyHoveredLinkIndex, LinkNormalColor);
            previouslyHoveredLinkIndex = -1; // Reset the link hovered caching so that in Update() it's set back to the hovered color
        }

        void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData)
        {
            CurrentlyHoveredOver = true;
            PointerIsDown = false;
            cachedCamera = eventData.enterEventCamera;
        }

        void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
        {
            CurrentlyHoveredOver = false;
            HoverEnded();
            SetAllLinksToNormalColor();
        }

        Camera cachedCamera;

        bool CurrentlyHoveredOver;
        bool PointerIsDown;

        void Update()
        {
            if (!CurrentlyHoveredOver)
                return;

            if (PointerIsDown)
                return;

            int linkIndex = TMP_TextUtilities.FindIntersectingLink(
                Text,
                Mouse.current.position.ReadValue(),
                cachedCamera
            );
            if (linkIndex < 0)
                HoverEnded();
            else
                HoverOnLink(linkIndex);
        }

        void SetAllLinksToNormalColor()
        {
            if (Text.textInfo == null || Text.textInfo.linkInfo == null) // Text object isn't initialized yet; required as of TMP 2.1
                return;

            for (int i = 0; i < Text.textInfo.linkInfo.Length; i++)
                SetLinkToColor(i, LinkNormalColor);
        }

        public event Action<string> OnLinkHovered;
        public event Action OnLinkHoverEnded;

        int previouslyHoveredLinkIndex = -1;

        void HoverOnLink(int linkIndex)
        {
            if (linkIndex < 0 || linkIndex >= Text.textInfo.linkInfo.Length)
                return;

            if (linkIndex == previouslyHoveredLinkIndex)
                return;

            previouslyHoveredLinkIndex = linkIndex;
            SetLinkToColor(linkIndex, LinkHoveredColor);

            string linkID = Text.textInfo.linkInfo[linkIndex].GetLinkID();
            OnLinkHovered?.Invoke(linkID);
        }

        void HoverEnded()
        {
            SetLinkToColor(previouslyHoveredLinkIndex, LinkNormalColor);

            if (previouslyHoveredLinkIndex > -1)
                OnLinkHoverEnded?.Invoke();

            previouslyHoveredLinkIndex = -1;
        }

        void SetLinkToColor(int linkIndex, Color32 color)
        {
            if (linkIndex < 0 || linkIndex >= Text.textInfo.linkInfo.Length)
                return;

            TMP_LinkInfo linkInfo = Text.textInfo.linkInfo[linkIndex];

            if (
                linkInfo.linkTextfirstCharacterIndex + linkInfo.linkTextLength - 1
                >= Text.textInfo.characterInfo.Length
            )
                return;

            for (int i = 0; i < linkInfo.linkTextLength; i++)
            {
                int characterIndex = linkInfo.linkTextfirstCharacterIndex + i;
                var charInfo = Text.textInfo.characterInfo[characterIndex];
                int meshIndex = charInfo.materialReferenceIndex;
                int vertexIndex = charInfo.vertexIndex;

                var characterVertexColors = Text.textInfo.meshInfo[meshIndex].colors32;

                if (charInfo.isVisible)
                {
                    characterVertexColors[vertexIndex + 0] = color;
                    characterVertexColors[vertexIndex + 1] = color;
                    characterVertexColors[vertexIndex + 2] = color;
                    characterVertexColors[vertexIndex + 3] = color;
                }
            }

            Text.UpdateVertexData(TMP_VertexDataUpdateFlags.All);
        }
    }
}
