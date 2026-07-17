using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hypocycloid.Ratioscope
{
    public sealed class CortexMessageBubble : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField]
        TMP_Text speakerLabel;

        [SerializeField]
        TMP_Text bodyLabel;

        string speaker;

        public bool IsCollapsible { get; private set; }
        public bool IsExpanded { get; private set; } = true;

        public TMP_Text Bind(string speaker, string body)
        {
            this.speaker = speaker;
            bodyLabel.text = body;
            IsCollapsible = string.Equals(speaker, "thinking", StringComparison.OrdinalIgnoreCase);
            SetExpanded(true);
            return bodyLabel;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (IsCollapsible && eventData.button == PointerEventData.InputButton.Left)
                SetExpanded(!IsExpanded);
        }

        public void SetExpanded(bool expanded)
        {
            IsExpanded = !IsCollapsible || expanded;
            speakerLabel.text = IsCollapsible ? $"{speaker} [{(IsExpanded ? "-" : "+")}]" : speaker;
            bodyLabel.gameObject.SetActive(IsExpanded);

            RectTransform bubbleRect = (RectTransform)transform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(bubbleRect);
            if (bubbleRect.parent is RectTransform messagesRect)
                LayoutRebuilder.MarkLayoutForRebuild(messagesRect);
        }
    }
}
