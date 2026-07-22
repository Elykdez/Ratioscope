using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Hypocycloid.UI;
using Hypocycloid.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Hypocycloid.Ratioscope
{
    /// <summary>
    /// Drives the authored CortexChat prefab. The prefab owns the complete UI hierarchy and
    /// serialized references; this component renders chat, status, context, and tooltip
    /// state from the scene AICortex, which owns the model and all LLM logic.
    /// </summary>
    public sealed class CortexChatController : MonoBehaviour
    {
        const double PerformanceStatusIntervalSeconds = 0.5d;
        const double BytesPerGiB = 1024d * 1024d * 1024d;
        const float CompactStatusWidth = 520f;
        const float CollapsedMessagesHeight = 56f;

        // Matches TipsTrigger's long-press timing so both tooltip paths feel the same on touch.
        const float TouchHoldSeconds = 0.5f;
        const float TouchHoldTolerance = 24f;

        // Clears a fingertip; the readout is a few short lines, so it sits above the contact.
        const float TouchAnchorLift = 130f;

        [Header("Model")]
        [SerializeField]
        [Tooltip("Scene AICortex that owns the model. Found automatically when empty.")]
        CortexCore aiCortex;

        [Header("Panel")]
        [SerializeField]
        GameObject panelRoot;

        [Header("Cortex")]
        [SerializeField]
        CortexMatrixView matrixView;

        [SerializeField]
        TMP_Text statusLabel;

        [SerializeField]
        RectTransform statusBar;

        [SerializeField]
        TMP_Text performanceLabel;

        [Header("Context")]
        [SerializeField]
        Button contextButton;

        [SerializeField]
        RawImage contextProgressImage;

        [SerializeField]
        TMP_Text contextButtonLabel;

        [SerializeField]
        GameObject contextPanel;

        [SerializeField]
        TMP_Text contextLabel;

        [SerializeField]
        Button compactButton;

        [SerializeField]
        [Tooltip(
            "Empty RectTransform moved to the pointer each frame; the shared tip anchors to it."
        )]
        RectTransform tooltipAnchor;

        [SerializeField]
        [Tooltip("TipsTrigger on the anchor. Registers itself with the scene TipSystem.")]
        TipsTrigger tooltipTrigger;

        [Header("Chat")]
        [SerializeField]
        ScrollRect messagesScroll;

        [SerializeField]
        RectTransform messagesContent;

        [SerializeField]
        LayoutElement messagesLayout;

        [SerializeField]
        GameObject messagesViewport;

        [SerializeField]
        Button messagesFoldButton;

        [SerializeField]
        Button messagesOpenButton;

        [SerializeField]
        CortexMessageBubble userBubblePrefab;

        [SerializeField]
        CortexMessageBubble aiBubblePrefab;

        [SerializeField]
        CortexMessageBubble thinkingBubblePrefab;

        [SerializeField]
        TMP_InputField input;

        [SerializeField]
        Button sendButton;

        [SerializeField]
        Button clearButton;

        [SerializeField]
        Button cancelButton;

        readonly PerformanceStatsSampler performanceStatsSampler = new();
        readonly List<ChatMessage> dialogue = new();

        UIConfigPanel configPanel;
        UIHelpPanel helpPanel;
        ChatStream activeStream;
        CortexMessageBubble activeThinkingBubble;
        TMP_Text activeThinkingLabel;
        TMP_Text activeReplyLabel;
        Material contextProgressMaterial;
        CortexCellInfo tooltipCell;
        bool touchActive;
        bool touchHoldQualified;
        bool touchHoldCancelled;
        float touchHoldStartTime;
        Vector2 touchHoldOrigin;
        bool subscribed;
        string readyStatus = "";
        string readyStatusCompact = "";
        string currentStatus = "";
        string currentCompactStatus = "";
        bool compactStatusLayout;
        int lastLayer;
        double lastTokenSeconds;
        int windowTokens;
        int windowCapacity;
        double nextPerformanceStatusAt;
        bool cortexIs3D;
        bool messagesCollapsed;
        float expandedMessagesMinHeight;
        float expandedMessagesPreferredHeight;
        float expandedMessagesFlexibleHeight;

        bool ModelReady => aiCortex != null && aiCortex.ModelReady;

        void Start()
        {
            configPanel = FindFirstObjectByType<UIConfigPanel>(FindObjectsInactive.Include);
            helpPanel = FindFirstObjectByType<UIHelpPanel>(FindObjectsInactive.Include);
            sendButton?.onClick.AddListener(OnSend);
            clearButton?.onClick.AddListener(OnClear);
            cancelButton?.onClick.AddListener(OnCancel);
            input?.onSubmit.AddListener(OnSubmit);
            contextButton?.onClick.AddListener(OnContextButton);
            compactButton?.onClick.AddListener(OnCompactContext);
            messagesFoldButton?.onClick.AddListener(OnMessagesFold);
            messagesOpenButton?.onClick.AddListener(OnMessagesOpen);

            if (messagesLayout != null)
            {
                expandedMessagesMinHeight = messagesLayout.minHeight;
                expandedMessagesPreferredHeight = messagesLayout.preferredHeight;
                expandedMessagesFlexibleHeight = messagesLayout.flexibleHeight;
            }

            if (contextProgressImage != null && contextProgressImage.material != null)
            {
                contextProgressMaterial = Instantiate(contextProgressImage.material);
                contextProgressImage.material = contextProgressMaterial;
            }

            panelRoot?.SetActive(true);
            Canvas.ForceUpdateCanvases();
            UpdateStatusLayout(true);
            if (tooltipTrigger != null)
            {
                tooltipTrigger.ContentProvider = BuildTooltipText;
                tooltipTrigger.HideTip();
            }
            contextPanel?.SetActive(false);
            matrixView?.FoldTo(false);
            SetMessagesCollapsed(false);
            SetSendEnabled(false);
            RefreshContextDisplay();

            if (aiCortex == null)
            {
                SetStatus(
                    "model load failed: no AICortex component in the scene",
                    "AICORTEX MISSING"
                );
                return;
            }

            SubscribeToCortex();
            aiCortex.EnsureModelLoading();
            PrepareMatrix(aiCortex.TransformerBlockCount);
        }

        void Update()
        {
            UpdateStatusLayout();
            UpdatePerformanceStatus();
            if (aiCortex != null && aiCortex.IsStreaming)
                UpdateStreamingStatus();
            // Chat generation runs in AICortex regardless; here we only pause the cortex render
            // when a full-screen overlay hides it from the user.
            matrixView?.SetRenderingSuppressed(IsOverlayOpen());
            UpdateTooltip();
        }

        // Settings and help render above the cortex as full-screen overlays.
        bool IsOverlayOpen()
        {
            return (configPanel != null && configPanel.Visible)
                || (helpPanel != null && helpPanel.Visible);
        }

        void LateUpdate()
        {
            if (!ModelReady || aiCortex.IsStreaming || input == null || !input.isFocused)
                return;
            if (!EnterPressedThisFrame() || ShiftPressed())
                return;

            int stringPosition = Mathf.Clamp(input.stringPosition, 0, input.text.Length);
            input.text = RemoveInsertedNewline(input.text, stringPosition);
            OnSend();
        }

        void SubscribeToCortex()
        {
            if (subscribed)
                return;

            subscribed = true;
            aiCortex.ModelLoadingStatusChanged += OnModelLoadingStatusChanged;
            aiCortex.ModelBecameReady += OnModelBecameReady;
            aiCortex.ModelLoadFailed += OnModelLoadFailed;
            aiCortex.StreamStarted += OnStreamStarted;
            aiCortex.StreamTokenSampled += OnStreamTokenSampled;
            aiCortex.StreamDetached += OnStreamDetached;
            aiCortex.ReplyCompleted += OnReplyCompleted;
            aiCortex.CompactionCompleted += OnCompactionCompleted;
            aiCortex.CompactionCancelled += OnCompactionCancelled;
            aiCortex.StreamStartFailed += OnStreamStartFailed;
            aiCortex.StreamFaulted += OnStreamFaulted;
        }

        void UnsubscribeFromCortex()
        {
            if (!subscribed || aiCortex == null)
                return;

            subscribed = false;
            aiCortex.ModelLoadingStatusChanged -= OnModelLoadingStatusChanged;
            aiCortex.ModelBecameReady -= OnModelBecameReady;
            aiCortex.ModelLoadFailed -= OnModelLoadFailed;
            aiCortex.StreamStarted -= OnStreamStarted;
            aiCortex.StreamTokenSampled -= OnStreamTokenSampled;
            aiCortex.StreamDetached -= OnStreamDetached;
            aiCortex.ReplyCompleted -= OnReplyCompleted;
            aiCortex.CompactionCompleted -= OnCompactionCompleted;
            aiCortex.CompactionCancelled -= OnCompactionCancelled;
            aiCortex.StreamStartFailed -= OnStreamStartFailed;
            aiCortex.StreamFaulted -= OnStreamFaulted;
        }

        void OnModelLoadingStatusChanged(string status)
        {
            string compactModel = FormatCompactSystemOption(aiCortex.SelectedSystemOption);
            string compactStatus = aiCortex.IsWaitingForModel
                ? "MODEL DOWNLOAD REQUIRED"
                : $"LOADING / {compactModel}";
            SetStatus(status.Replace('\n', ' '), compactStatus);
        }

        void OnModelBecameReady(ChatRuntimeInfo info)
        {
            readyStatus =
                $"ready | {aiCortex.SelectedSystemOption} | {Path.GetFileName(info.ModelSource)} | "
                + $"{info.Backend} | "
                + $"{info.TransformerBlockCount} blocks | window {info.ContextLength} | "
                + $"loaded in {info.LoadSeconds:0.0}s";
            readyStatusCompact =
                $"READY / {FormatCompactSystemOption(aiCortex.SelectedSystemOption)}";
            windowCapacity = info.ContextLength;
            PrepareMatrix(info.TransformerBlockCount);
            SetSendEnabled(true);
            SetStatus(readyStatus, readyStatusCompact);
            RefreshContextDisplay();
        }

        void PrepareMatrix(int transformerBlockCount)
        {
            if (matrixView == null)
                return;
            if (aiCortex.CortexVisualization != null)
                matrixView.Configure(aiCortex.CortexVisualization);
            if (
                transformerBlockCount > 0
                && (
                    matrixView.Grid == null
                    || matrixView.Grid.TransformerBlockCount != transformerBlockCount
                )
            )
                matrixView.Prepare(transformerBlockCount);
        }

        void OnModelLoadFailed(Exception exception)
        {
            SetStatus("model load failed: " + exception.Message, "MODEL LOAD FAILED");
        }

        void UpdateStreamingStatus()
        {
            ChatStream stream = aiCortex.ActiveStream;
            if (stream == null || stream.State != ChatStreamState.Thinking)
                return;

            string activity =
                aiCortex.ActiveStreamPurpose == StreamPurpose.Compaction
                    ? "compacting context"
                    : "thinking";
            SetStatus(
                $"{activity} | graph op {lastLayer + 1}/{stream.LayerCount}"
                    + $" | token {stream.GeneratedIds.Count}"
                    + $" | {lastTokenSeconds:0.0}s/token"
                    + $" | window {windowTokens}/{windowCapacity}"
                    + $" | entropy {matrixView.Grid.SmoothedEntropy:0.00}",
                $"{activity.ToUpperInvariant()} / {stream.GeneratedIds.Count} TOK"
            );
        }

        void OnSubmit(string _) => OnSend();

        void OnSend()
        {
            if (!ModelReady || aiCortex.IsStreaming || input == null)
                return;
            string text = input.text.Trim();
            if (text.Length == 0)
                return;

            input.text = "";
            SetSendEnabled(false);
            AddBubble(userBubblePrefab, "you", text);
            dialogue.Add(ChatMessage.User(text));
            if (aiCortex.SupportsThinking && aiCortex.EnableThinking)
            {
                activeThinkingLabel = AddBubble(
                    thinkingBubblePrefab,
                    "thinking",
                    "...",
                    out activeThinkingBubble
                );
            }
            activeReplyLabel = AddBubble(aiBubblePrefab, "ai", "...");

            aiCortex.BeginReply(text);
            RefreshContextDisplay();
        }

        void OnClear()
        {
            if (aiCortex == null || aiCortex.IsStreaming)
                return;

            aiCortex.ClearHistory();
            dialogue.Clear();
            ClearBubbles();

            if (ModelReady)
                SetStatus(readyStatus, readyStatusCompact);
            RefreshContextDisplay();
        }

        void OnCancel()
        {
            if (aiCortex == null || !aiCortex.IsStreaming)
                return;
            aiCortex.CancelActiveStream();
        }

        void OnCompactionCancelled()
        {
            if (ModelReady)
                SetStatus(readyStatus, readyStatusCompact);
            SetSendEnabled(ModelReady);
            RefreshContextDisplay();
        }

        void OnStreamStarted(ChatStream stream, StreamPurpose purpose)
        {
            activeStream = stream;
            lastLayer = 0;
            lastTokenSeconds = 0;
            windowTokens = stream.PromptTokens;
            windowCapacity = stream.WindowCapacity;
            matrixView.Attach(stream);
            stream.LayerExecuted += OnLayerExecuted;
            UpdateContextMeter(windowTokens, windowCapacity);
            // Stream is now attached, so IsStreaming is true: flip the input row to cancel.
            SetSendEnabled(false);
        }

        void OnLayerExecuted(int index, int count, string opName) => lastLayer = index;

        void OnStreamTokenSampled(TokenMetrics metrics, string live, string liveThinking)
        {
            lastTokenSeconds = metrics.ElapsedSeconds;
            windowTokens = metrics.WindowTokens;
            windowCapacity = metrics.WindowCapacity;
            bool reply = aiCortex.ActiveStreamPurpose == StreamPurpose.Reply;
            if (reply && activeReplyLabel != null)
            {
                if (activeThinkingLabel != null)
                    activeThinkingLabel.text = liveThinking.Length == 0 ? "..." : liveThinking;
                activeReplyLabel.text = live.Length == 0 ? "..." : live;
            }
            UpdateContextMeter(windowTokens, windowCapacity);
            if (contextPanel != null && contextPanel.activeSelf)
                RefreshContextDisplay(reply ? live : null);
            ScrollToBottom();
        }

        void OnStreamDetached()
        {
            if (activeStream != null)
                activeStream.LayerExecuted -= OnLayerExecuted;
            activeStream = null;
            matrixView?.Detach();
        }

        void OnReplyCompleted(ChatResult result)
        {
            if (activeThinkingLabel != null)
            {
                if (result.Thinking.Length > 0)
                    activeThinkingLabel.text = result.Thinking;
                else if (activeThinkingBubble != null)
                    Destroy(activeThinkingBubble.gameObject);
            }
            if (activeReplyLabel != null)
                activeReplyLabel.text = result.Content.Length == 0 ? "(empty)" : result.Content;
            dialogue.Add(ChatMessage.Assistant(result.Content));
            string finishStatus = result.FinishReason switch
            {
                ChatFinishReason.StopToken => "",
                ChatFinishReason.Cancelled => " | cancelled",
                ChatFinishReason.ContextLimit => " | context full",
                _ => " | token limit",
            };
            SetStatus(
                readyStatus
                    + $" | last reply {result.GeneratedTokens} tokens in {result.ElapsedSeconds:0.0}s"
                    + finishStatus,
                $"READY / {result.GeneratedTokens} TOK / {result.ElapsedSeconds:0.0}s"
                    + finishStatus.ToUpperInvariant()
            );
            activeThinkingBubble = null;
            activeThinkingLabel = null;
            activeReplyLabel = null;
            SetSendEnabled(ModelReady);
            RefreshContextDisplay();
            ScrollToBottom();
        }

        void OnCompactionCompleted(ChatResult result, bool memoryUpdated)
        {
            RefreshContextDisplay();
            SetStatus(
                memoryUpdated
                    ? readyStatus + $" | context compacted to {result.GeneratedTokens} tokens"
                    : readyStatus + " | context compaction returned empty",
                memoryUpdated ? "READY / MEMORY UPDATED" : "MEMORY UPDATE EMPTY"
            );
            SetSendEnabled(ModelReady);
        }

        void OnStreamFaulted(Exception exception)
        {
            if (exception != null)
                SetStatus("generation failed: " + exception.Message, "GENERATION FAILED");
            if (activeReplyLabel != null && activeReplyLabel.text == "...")
                activeReplyLabel.text = "(failed)";
            if (activeThinkingBubble != null)
                Destroy(activeThinkingBubble.gameObject);
            activeThinkingBubble = null;
            activeThinkingLabel = null;
            activeReplyLabel = null;
            SetSendEnabled(ModelReady);
            RefreshContextDisplay();
        }

        void OnStreamStartFailed(Exception exception)
        {
            SetStatus("cannot start: " + exception.Message, "START FAILED");
            if (activeReplyLabel != null && activeReplyLabel.text == "...")
                activeReplyLabel.text = "(failed)";
            if (activeThinkingBubble != null)
                Destroy(activeThinkingBubble.gameObject);
            activeThinkingBubble = null;
            activeThinkingLabel = null;
            activeReplyLabel = null;
            SetSendEnabled(true);
            RefreshContextDisplay();
        }

        void OnContextButton()
        {
            if (contextPanel == null)
                return;
            contextPanel.SetActive(!contextPanel.activeSelf);
            RefreshContextDisplay();
        }

        public void ToggleDimension()
        {
            cortexIs3D = !cortexIs3D;
            matrixView?.FoldTo(cortexIs3D);
            SetMessagesCollapsed(cortexIs3D);
        }

        public void ImportDialogue()
        {
            if (aiCortex == null || aiCortex.IsStreaming)
            {
                SetStatus(
                    "cancel the current generation before importing a dialogue",
                    "IMPORT BLOCKED"
                );
                return;
            }

            LoadManager.Ins.OpenTextFile(
                ApplyImportedDialogue,
                error => SetStatus("import failed: " + error, "IMPORT FAILED")
            );
        }

        public void ExportDialogue()
        {
            if (aiCortex != null && aiCortex.IsStreaming)
            {
                SetStatus(
                    "cancel the current generation before exporting a dialogue",
                    "EXPORT BLOCKED"
                );
                return;
            }

            LoadManager.Ins.SaveTextFile(
                $"Ratioscope-dialogue-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                ChatDialogueFile.Serialize(dialogue),
                fileName =>
                    SetStatus(
                        $"exported {dialogue.Count} messages to {fileName}",
                        $"EXPORTED / {dialogue.Count} MSG"
                    ),
                error => SetStatus("export failed: " + error, "EXPORT FAILED")
            );
        }

        void ApplyImportedDialogue(string text, string sourceName)
        {
            if (
                !ChatDialogueFile.TryDeserialize(
                    text,
                    out List<ChatMessage> loaded,
                    out string error
                )
            )
            {
                SetStatus("import failed: " + error, "IMPORT FAILED");
                return;
            }
            if (!aiCortex.ReplaceConversation(loaded))
            {
                SetStatus("import failed: dialogue is busy", "IMPORT FAILED");
                return;
            }

            dialogue.Clear();
            dialogue.AddRange(loaded);
            RebuildDialogueBubbles();
            SetStatus(
                $"imported {loaded.Count} messages from {sourceName}",
                $"IMPORTED / {loaded.Count} MSG"
            );
            RefreshContextDisplay();
        }

        void OnMessagesFold()
        {
            SetMessagesCollapsed(true);
        }

        void OnMessagesOpen()
        {
            SetMessagesCollapsed(false);
        }

        void SetMessagesCollapsed(bool collapsed)
        {
            messagesCollapsed = collapsed;
            messagesViewport?.SetActive(true);

            if (messagesLayout != null)
            {
                messagesLayout.minHeight = collapsed
                    ? CollapsedMessagesHeight
                    : expandedMessagesMinHeight;
                messagesLayout.preferredHeight = collapsed
                    ? CollapsedMessagesHeight
                    : expandedMessagesPreferredHeight;
                messagesLayout.flexibleHeight = collapsed ? 0f : expandedMessagesFlexibleHeight;

                if (messagesLayout.transform.parent is RectTransform layoutRoot)
                    LayoutRebuilder.MarkLayoutForRebuild(layoutRoot);
            }

            messagesFoldButton?.gameObject.SetActive(!collapsed);
            messagesOpenButton?.gameObject.SetActive(collapsed);

            if (collapsed)
                ScrollToBottom();
        }

        void OnCompactContext()
        {
            if (!ModelReady || aiCortex.IsStreaming)
                return;
            contextPanel?.SetActive(false);
            SetSendEnabled(false);
            if (!aiCortex.BeginCompaction())
            {
                SetStatus(readyStatus + " | no complete turn to compact", "NOTHING TO COMPACT");
                SetSendEnabled(ModelReady);
            }
        }

        void RefreshContextDisplay(string liveReply = null)
        {
            if (!ModelReady || aiCortex.Tokenizer == null)
            {
                UpdateContextMeter(0, Math.Max(1, windowCapacity));
                if (contextLabel != null)
                    contextLabel.text = "Context becomes available after the model loads.";
                if (compactButton != null)
                    compactButton.interactable = false;
                if (contextButton != null)
                    contextButton.interactable = false;
                return;
            }

            if (contextButton != null)
                contextButton.interactable = true;

            try
            {
                ChatContextSnapshot context = aiCortex.InspectContext();
                bool streaming = aiCortex.IsStreaming;
                int used = streaming ? windowTokens : context.PromptTokens;
                int capacity = streaming ? windowCapacity : context.WindowCapacity;
                UpdateContextMeter(used, capacity);

                if (contextLabel != null)
                {
                    StringBuilder text = new();
                    text.Append("<b>CURRENT MODEL CONTEXT</b>  ")
                        .Append(context.PromptTokens)
                        .Append('/')
                        .Append(context.WindowCapacity)
                        .Append(" prompt tokens | ")
                        .Append(context.AvailableReplyTokens)
                        .Append(" available for reply\n")
                        .Append("compaction: manual");
                    if (context.WasTrimmed)
                        text.Append(" | ")
                            .Append(context.RemovedMessages.Count)
                            .Append(" messages outside current window");

                    foreach (ChatMessage message in context.Messages)
                    {
                        string role = aiCortex.IsMemoryMessage(message)
                            ? "MEMORY"
                            : message.Role.ToUpperInvariant();
                        text.Append("\n\n<b>")
                            .Append(role)
                            .Append("</b>\n")
                            .Append(EscapeMarkup(message.Content));
                    }
                    if (!string.IsNullOrEmpty(liveReply))
                        text.Append("\n\n<b>ASSISTANT (GENERATING)</b>\n")
                            .Append(EscapeMarkup(liveReply));
                    contextLabel.text = text.ToString();
                }
            }
            catch (Exception exception)
            {
                if (contextLabel != null)
                    contextLabel.text = "Context unavailable: " + EscapeMarkup(exception.Message);
            }

            if (compactButton != null)
                compactButton.interactable = !aiCortex.IsStreaming && aiCortex.HasCompletableTurn;
        }

        void UpdateContextMeter(int usedTokens, int capacity)
        {
            float occupancy = capacity > 0 ? Mathf.Clamp01((float)usedTokens / capacity) : 0f;
            if (contextButtonLabel != null)
                contextButtonLabel.text = $"{Mathf.RoundToInt(occupancy * 100f)}%";
            if (contextProgressMaterial == null)
                return;

            contextProgressMaterial.SetFloat("_Progress", occupancy);
            Color calm = new(0.18f, 2.4f, 1.7f, 1f);
            Color hot = new(4.5f, 0.7f, 0.25f, 1f);
            contextProgressMaterial.SetColor("_ArcColor", Color.Lerp(calm, hot, occupancy));
            contextProgressMaterial.SetColor("_HeadColor", Color.Lerp(calm, hot, occupancy));
        }

        TMP_Text AddBubble(CortexMessageBubble prefab, string speaker, string text)
        {
            return AddBubble(prefab, speaker, text, out _);
        }

        TMP_Text AddBubble(
            CortexMessageBubble prefab,
            string speaker,
            string text,
            out CortexMessageBubble bubble
        )
        {
            bubble = null;
            if (prefab == null || messagesContent == null)
                return null;

            bubble = Instantiate(prefab, messagesContent);
            TMP_Text body = bubble.Bind(speaker, text);
            ScrollToBottom();
            return body;
        }

        void ClearBubbles()
        {
            if (messagesContent == null)
                return;
            for (int i = messagesContent.childCount - 1; i >= 0; i--)
                Destroy(messagesContent.GetChild(i).gameObject);
        }

        void RebuildDialogueBubbles()
        {
            ClearBubbles();
            for (int i = 0; i < dialogue.Count; i++)
            {
                ChatMessage message = dialogue[i];
                AddBubble(
                    message.Role == "user" ? userBubblePrefab : aiBubblePrefab,
                    message.Role == "user" ? "you" : "ai",
                    message.Content.Length == 0 ? "(empty)" : message.Content
                );
            }
            ScrollToBottom();
        }

        void UpdateTooltip()
        {
            if (panelRoot == null || !panelRoot.activeInHierarchy || tooltipTrigger == null)
                return;

            // Settings and help render above the cortex; suppress cell tooltips while open.
            if (IsOverlayOpen())
            {
                HideCellTip();
                return;
            }

            if (
                !TryGetTipPointer(out Vector2 pointer, out bool isTouch)
                || matrixView == null
                || !matrixView.TryGetCell(pointer, null, out CortexCellInfo info)
            )
            {
                HideCellTip();
                return;
            }

            tooltipCell = info;
            MoveAnchorTo(pointer, isTouch);

            // Re-showing each frame re-runs ContentProvider and repositions against the anchor,
            // which is what keeps the readout live while the pointer moves across cells.
            tooltipTrigger.ShowTip();
        }

        /// <summary>
        /// Only hides when the cell readout is the tip currently on screen. The TipSystem is
        /// shared, so hiding unconditionally every frame would stomp button and slider tooltips.
        /// </summary>
        void HideCellTip()
        {
            if (tooltipTrigger.TipVisible)
                tooltipTrigger.HideTip();
        }

        void MoveAnchorTo(Vector2 pointer, bool isTouch)
        {
            if (tooltipAnchor == null || tooltipAnchor.parent is not RectTransform parent)
                return;

            // The tip pivots from its top-left, so it grows down-right of the anchor. Under a
            // finger that would land beneath the contact point, so touch lifts the anchor above
            // it instead.
            Vector2 screenPoint =
                pointer + (isTouch ? new Vector2(18f, TouchAnchorLift) : new Vector2(18f, -18f));

            // RectTransform.position is world space; convert the screen point through the canvas
            // camera so the anchor tracks the pointer under Screen Space - Camera.
            if (
                RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    parent,
                    screenPoint,
                    matrixView.EventCamera,
                    out Vector3 anchorWorld
                )
            )
                tooltipAnchor.position = anchorWorld;
        }

        string BuildTooltipText()
        {
            if (matrixView == null)
                return "";

            CortexCellInfo info = tooltipCell;
            string title =
                info.TransformerBlock >= 0
                    ? $"block {info.TransformerBlock + 1}/{matrixView.Grid.TransformerBlockCount}"
                        + $" | {info.Branch} | {info.Stage}"
                    : "token surface";
            string token = "";
            if (info.TokenId >= 0 && aiCortex != null && aiCortex.Tokenizer != null)
            {
                string decoded = aiCortex
                    .Tokenizer.Decode(new[] { info.TokenId }, skipSpecialTokens: false)
                    .Replace("\n", "\\n");
                token = $"\n'{EscapeMarkup(decoded)}' | {info.Probability:P0}";
            }
            string candidates = FormatCandidates(matrixView.Grid.LastCandidates);
            return $"<b>{title}</b>\nheat {info.Heat:0.00}{token}"
                + $"\nentropy {matrixView.Grid.SmoothedEntropy:0.00}{candidates}";
        }

        /// <summary>
        /// Mouse hover reveals the readout immediately. Touch requires a stationary hold, so
        /// dragging across the matrix does not flash tooltips; this mirrors TipsTrigger's
        /// LongPress mode rather than inventing a second gesture.
        /// </summary>
        bool TryGetTipPointer(out Vector2 position, out bool isTouch)
        {
            position = default;
            isTouch = false;

            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null && touchscreen.primaryTouch.press.isPressed)
            {
                isTouch = true;
                position = touchscreen.primaryTouch.position.ReadValue();

                if (!touchActive)
                {
                    touchActive = true;
                    touchHoldQualified = false;
                    touchHoldCancelled = false;
                    touchHoldStartTime = Time.unscaledTime;
                    touchHoldOrigin = position;
                }
                else if (!touchHoldQualified && !touchHoldCancelled)
                {
                    if (
                        (position - touchHoldOrigin).sqrMagnitude
                        > TouchHoldTolerance * TouchHoldTolerance
                    )
                        touchHoldCancelled = true;
                    else if (Time.unscaledTime - touchHoldStartTime >= TouchHoldSeconds)
                        touchHoldQualified = true;
                }

                return touchHoldQualified;
            }

            touchActive = false;

            Mouse mouse = Mouse.current;
            if (mouse == null)
                return false;

            position = mouse.position.ReadValue();
            return true;
        }

        void SetStatus(string text, string compactText = null)
        {
            currentStatus = text;
            currentCompactStatus = compactText ?? text;
            if (statusLabel != null)
                statusLabel.text = compactStatusLayout ? currentCompactStatus : currentStatus;
        }

        void UpdateStatusLayout(bool force = false)
        {
            if (statusBar == null)
                return;

            bool compact = statusBar.rect.width < CompactStatusWidth;
            if (!force && compact == compactStatusLayout)
                return;

            compactStatusLayout = compact;
            if (statusLabel != null)
                statusLabel.text = compact ? currentCompactStatus : currentStatus;
            nextPerformanceStatusAt = 0;
        }

        void UpdatePerformanceStatus()
        {
            if (performanceLabel == null)
                return;

            PerformanceStats stats = performanceStatsSampler.Sample();
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextPerformanceStatusAt)
                return;

            nextPerformanceStatusAt = now + PerformanceStatusIntervalSeconds;
            performanceLabel.text = compactStatusLayout
                ? $"{Mathf.RoundToInt(stats.Fps)} FPS / {FormatVram(stats.GraphicsMemoryBytes, true)}"
                : $"FPS {Mathf.RoundToInt(stats.Fps)} | VRAM {FormatVram(stats.GraphicsMemoryBytes)}";
        }

        static string FormatVram(long allocatedBytes, bool compact = false)
        {
            int capacityMegabytes = SystemInfo.graphicsMemorySize;
            string unit = compact ? "G" : " GiB";
            if (allocatedBytes > 0 && capacityMegabytes > 0)
                return $"{allocatedBytes / BytesPerGiB:0.0}/{capacityMegabytes / 1024d:0.0}{unit}";
            if (allocatedBytes > 0)
                return $"{allocatedBytes / BytesPerGiB:0.0}{unit}";
            if (capacityMegabytes > 0)
                return $"~{capacityMegabytes / 1024d:0.0}{unit}";
            return "n/a";
        }

        static string FormatCompactSystemOption(LlmSystemOption option)
        {
            return option switch
            {
                LlmSystemOption.Gpu1_7B_2048 => "1.7B GPU",
                LlmSystemOption.Cpu1_7B_2048 => "1.7B CPU",
                LlmSystemOption.Gpu4B_4096 => "4B GPU",
                LlmSystemOption.Cpu4B_4096 => "4B CPU",
                _ => option.ToString(),
            };
        }

        // While a stream runs, send hides and cancel takes its place; clear and compact stay
        // disabled. Outside streaming the row returns to send with clear/compact gated by
        // readiness. Cancel visibility keys off IsStreaming so both reply and compaction show it.
        void SetSendEnabled(bool enabled)
        {
            bool streaming = aiCortex != null && aiCortex.IsStreaming;
            if (sendButton != null)
            {
                sendButton.gameObject.SetActive(!streaming);
                sendButton.interactable = enabled;
            }
            if (cancelButton != null)
                cancelButton.gameObject.SetActive(streaming);
            if (clearButton != null)
                clearButton.interactable = enabled && !streaming;
            if (compactButton != null)
                compactButton.interactable =
                    enabled && !streaming && ModelReady && aiCortex.HasCompletableTurn;
        }

        void ScrollToBottom()
        {
            if (messagesScroll == null)
                return;
            Canvas.ForceUpdateCanvases();
            messagesScroll.verticalNormalizedPosition = 0f;
        }

        static string FormatCandidates(IReadOnlyList<TokenCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                return "";

            // One candidate per line. The shared tip sizes itself to its content, so keeping
            // these on a single row would stretch it far wider than a portrait screen.
            StringBuilder text = new("\ntop");
            int count = Mathf.Min(5, candidates.Count);
            for (int i = 0; i < count; i++)
            {
                string token = EscapeMarkup(candidates[i].Text.Replace("\n", "\\n"));
                text.Append("\n[")
                    .Append(token)
                    .Append(' ')
                    .Append(candidates[i].Probability.ToString("P0"))
                    .Append(']');
            }
            return text.ToString();
        }

        static string EscapeMarkup(string text) => text.Replace("<", "&lt;").Replace(">", "&gt;");

        static string RemoveInsertedNewline(string text, int stringPosition)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? "";

            int index = Mathf.Clamp(stringPosition - 1, 0, text.Length - 1);
            if (text[index] == '\n')
            {
                int start = index > 0 && text[index - 1] == '\r' ? index - 1 : index;
                return text.Remove(start, index - start + 1);
            }
            if (text[index] == '\r')
                return text.Remove(index, 1);
            return text;
        }

        static bool EnterPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            return keyboard != null
                && (
                    keyboard.enterKey.wasPressedThisFrame
                    || keyboard.numpadEnterKey.wasPressedThisFrame
                );
#else
            return UnityEngine.Input.GetKeyDown(KeyCode.Return)
                || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter);
#endif
        }

        static bool ShiftPressed()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            return keyboard != null
                && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
#else
            return UnityEngine.Input.GetKey(KeyCode.LeftShift)
                || UnityEngine.Input.GetKey(KeyCode.RightShift);
#endif
        }

        void OnDestroy()
        {
            sendButton?.onClick.RemoveListener(OnSend);
            clearButton?.onClick.RemoveListener(OnClear);
            cancelButton?.onClick.RemoveListener(OnCancel);
            input?.onSubmit.RemoveListener(OnSubmit);
            contextButton?.onClick.RemoveListener(OnContextButton);
            compactButton?.onClick.RemoveListener(OnCompactContext);
            messagesFoldButton?.onClick.RemoveListener(OnMessagesFold);
            messagesOpenButton?.onClick.RemoveListener(OnMessagesOpen);

            if (activeStream != null)
                activeStream.LayerExecuted -= OnLayerExecuted;
            activeStream = null;
            UnsubscribeFromCortex();
            if (contextProgressMaterial != null)
                Destroy(contextProgressMaterial);
        }
    }
}
