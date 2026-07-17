using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hypocycloid.UI
{
    /// <summary>
    /// Allows links in TextMeshPro text objects to be clicked on, and gives them custom colors when they are hovered or clicked.
    /// </summary>
    [RequireComponent(typeof(UITextHyperlink))]
    [DisallowMultipleComponent]
    public class SimpleLinkBehavior : MonoBehaviour
    {
        readonly Dictionary<string, Action> CustomLinks = new();

        void Awake()
        {
            GetComponent<UITextHyperlink>().OnLinkClicked += ClickOnLink;
        }

        void ClickOnLink(string linkID)
        {
            if (CustomLinks.TryGetValue(linkID, out var action))
                action?.Invoke();
            else if (!string.IsNullOrEmpty(linkID))
                Application.OpenURL(linkID);
        }

        /// <summary>
        /// Sets some code to be run when a link is clicked on.
        /// </summary>
        /// <param name="linkID"></param>
        /// <param name="linkAction"></param>
        public void SetCustomLink(string linkID, Action linkAction)
        {
            CustomLinks[linkID] = linkAction;
        }
    }
}
