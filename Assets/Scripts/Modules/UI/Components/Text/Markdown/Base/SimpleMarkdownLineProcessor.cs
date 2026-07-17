using System.Collections.Generic;

namespace Hypocycloid.UI
{
    internal abstract class SimpleMarkdownLineProcessor : MarkdownLineProcessorBase
    {
        protected override void ProcessInternal(
            IReadOnlyList<MarkdownLine> lines,
            MarkdownRenderingSettings settings
        )
        {
            foreach (var line in lines)
            {
                if (line.DisableFutureProcessing)
                    continue;

                ProcessLine(line, settings);
            }
        }

        protected abstract void ProcessLine(MarkdownLine line, MarkdownRenderingSettings settings);
    }
}
