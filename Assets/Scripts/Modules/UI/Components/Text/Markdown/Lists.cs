using System;
using System.Collections.Generic;
using System.Text;
using Hypocycloid.Utils;

namespace Hypocycloid.UI
{
    abstract class MarkdownListProcessor : MarkdownLineProcessorBase
    {
        protected abstract bool LineIsPartOfList(
            StringBuilder lineBuilder,
            MarkdownRenderingSettings settings
        );
        protected abstract void FormatBeginningOfListLine(
            StringBuilder lineBuilder,
            int listLineIndex,
            MarkdownRenderingSettings settings
        );

        protected abstract void InsertListBulletContents(
            StringBuilder builder,
            int index,
            int listLineIndex,
            MarkdownRenderingSettings settings,
            out int bulletTextLength
        );

        protected override void ProcessInternal(
            IReadOnlyList<MarkdownLine> lines,
            MarkdownRenderingSettings settings
        )
        {
            bool previousLineWasListLine = false;
            int listLineCount = 0;

            // Render list items as plain "<bullet> <content>" lines using standard rich text.
            // The previous right-aligned bullet hack relied on <line-height=0%>/<width>/<align>/
            // <indent> with an embedded newline; the zero-height line corrupted TMP's height
            // metrics, which truncated long documents with an ellipsis.

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].DisableFutureProcessing)
                    goto lineNotPartOfList;

                StringBuilder builder = lines[i].Builder;

                if (builder.IsEmptyOrWhitespace())
                    goto lineNotPartOfList;

                if (LineIsPartOfList(builder, settings))
                {
                    if (previousLineWasListLine)
                        listLineCount++;

                    FormatBeginningOfListLine(builder, listLineCount, settings);
                    builder.TrimStart(' ');

                    // Prepend "<bullet> " to the content; no TMP layout tags.
                    builder.Insert(0, " ");
                    InsertListBulletContents(builder, index: 0, listLineCount, settings, out _);

                    previousLineWasListLine = true;
                }
                else
                {
                    goto lineNotPartOfList;
                }

                continue;

                lineNotPartOfList:

                if (previousLineWasListLine)
                    HandleListEnds(listEndLineIndex: i - 1);

                previousLineWasListLine = false;
                listLineCount = 0;
                ResetVariables();
                continue;
            }

            if (previousLineWasListLine)
                HandleListEnds(listEndLineIndex: lines.Count - 1);

            // Handles the vertical offset of the list from its surrounding content
            void HandleListEnds(int listEndLineIndex)
            {
                int listStartLineIndex = listEndLineIndex - listLineCount;

                // If the list doesn't start on the first line, add whitespace before the first list line.
                if (listStartLineIndex > 0)
                    lines[listStartLineIndex]
                        .AddVerticalWhitespaceBefore(settings.Lists.VerticalOffset);

                // If the list doesn't end on the last line, add whitespace after the last line.
                if (listEndLineIndex < lines.Count - 1)
                    lines[listEndLineIndex]
                        .AddVerticalWhitespaceAfter(settings.Lists.VerticalOffset);

                // Remove extra whitespace before and after the list.

                for (int i = listStartLineIndex - 1; i > 0; i--)
                {
                    // The use of IsEmpty() instead of IsEmptyOrWhitespace() is intentional. It allows you to manually add vertical offset to lists by putting
                    // a single space on the line. However, this is a deviation from standard markdown implementations.
                    if (lines[i].Builder.IsEmpty())
                        lines[i].DeleteLineAfterProcessing = true;
                    else
                        break;
                }

                for (int i = listEndLineIndex + 1; i < lines.Count; i++)
                {
                    if (lines[i].Builder.IsEmpty())
                        lines[i].DeleteLineAfterProcessing = true;
                    else
                        break;
                }
            }
        }

        protected virtual void ResetVariables() { }
    }

    class UnorderedLists : MarkdownListProcessor
    {
        protected override bool LineIsPartOfList(
            StringBuilder lineBuilder,
            MarkdownRenderingSettings settings
        ) => lineBuilder.StartsWith("- ") || lineBuilder.StartsWith("* ");

        protected override void FormatBeginningOfListLine(
            StringBuilder lineBuilder,
            int listLineIndex,
            MarkdownRenderingSettings settings
        )
        {
            // Remove the '-' or '*'
            lineBuilder.Remove(0, 1);
        }

        protected override void InsertListBulletContents(
            StringBuilder builder,
            int index,
            int _,
            MarkdownRenderingSettings settings,
            out int bulletTextLength
        )
        {
            builder.Insert(index, settings.Lists.UnorderedListBullet);
            bulletTextLength = settings.Lists.UnorderedListBullet.Length;
        }

        protected override bool AllowedToProces(MarkdownRenderingSettings settings) =>
            settings.Lists.RenderUnorderedLists;
    }

    class OrderedLists : MarkdownListProcessor
    {
        int dotIndex;
        int parsedNumber;
        bool currentListIsAutoNumbered;
        string potentialNumberString;

        protected override void ResetVariables()
        {
            dotIndex = 0;
            parsedNumber = 0;
            currentListIsAutoNumbered = false;
            potentialNumberString = string.Empty;
        }

        protected override bool LineIsPartOfList(
            StringBuilder lineBuilder,
            MarkdownRenderingSettings settings
        )
        {
            dotIndex = lineBuilder.IndexOf('.');

            if (dotIndex < 1)
                return false;

            if (lineBuilder.Length < dotIndex + 2 || lineBuilder[dotIndex + 1] != ' ')
                return false;

            potentialNumberString = lineBuilder.Snip(0, dotIndex - 1);
            bool thisLineIsListLine = int.TryParse(potentialNumberString, out parsedNumber);

            return thisLineIsListLine;
        }

        protected override void FormatBeginningOfListLine(
            StringBuilder lineBuilder,
            int listLineIndex,
            MarkdownRenderingSettings settings
        )
        {
            lineBuilder.Remove(0, dotIndex + 1);
        }

        protected override void InsertListBulletContents(
            StringBuilder builder,
            int index,
            int listLineIndex,
            MarkdownRenderingSettings settings,
            out int bulletTextLength
        )
        {
            // Our logic here differs from other markdown implementations: if you use all 1s for your ordered list, it auto-numbers them. Otherwise,
            // it uses the numbers you provide.

            string numberToDisplayString;

            int listLineNumber = listLineIndex + 1;
            bool isFirstLineOfList = listLineIndex == 0;
            if (isFirstLineOfList)
            {
                if (parsedNumber == 1)
                {
                    currentListIsAutoNumbered = true;
                    numberToDisplayString = listLineNumber.ToString();
                }
                else
                {
                    numberToDisplayString = potentialNumberString;
                }
            }
            else
            {
                if (currentListIsAutoNumbered && parsedNumber == 1)
                {
                    numberToDisplayString = listLineNumber.ToString();
                }
                else
                {
                    numberToDisplayString = potentialNumberString;
                    currentListIsAutoNumbered = false;
                }
            }

            builder.InsertChain(
                index,
                numberToDisplayString,
                settings.Lists.OrderedListNumberSuffix
            );
            bulletTextLength =
                numberToDisplayString.Length + settings.Lists.OrderedListNumberSuffix.Length;
        }

        protected override bool AllowedToProces(MarkdownRenderingSettings settings) =>
            settings.Lists.RenderOrderedLists;
    }
}
