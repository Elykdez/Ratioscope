using TMPro;
using UnityEngine;

namespace Hypocycloid.UI
{
    [RequireComponent(typeof(TMP_Text))]
    public class UIMarkdownRenderer : MonoBehaviour
    {
        [SerializeField]
        [TextArea(minLines: 5, maxLines: 20)]
        string source;

        // Update this if while live editing!!!
        public string Source
        {
            get => source;
            set
            {
                source = value;
                RenderText();
            }
        }

        TMP_Text textMesh;
        public TMP_Text TextMesh
        {
            get
            {
                if (textMesh == null)
                    textMesh = GetComponent<TMP_Text>();

                return textMesh;
            }
        }

        public MarkdownRenderingSettings RenderSettings = MarkdownRenderingSettings.Default;

        void OnValidate()
        {
            RenderText();
        }

        void RenderText()
        {
            Markdown.RenderToTextMesh(Source, TextMesh, RenderSettings);
        }
    }
}
