using System.Collections.Generic;

namespace Hypocycloid.UI
{
    internal abstract class MarkdownLineProcessorBase
    {
        public void Process(IReadOnlyList<MarkdownLine> lines, MarkdownRenderingSettings settings)
        {
            if (!AllowedToProces(settings))
                return;

            ProcessInternal(lines, settings);
        }

        protected virtual bool AllowedToProces(MarkdownRenderingSettings settings) => true;

        protected abstract void ProcessInternal(
            IReadOnlyList<MarkdownLine> lines,
            MarkdownRenderingSettings settings
        );
    }
}
