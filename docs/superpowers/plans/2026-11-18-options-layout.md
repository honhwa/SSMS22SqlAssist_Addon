# Options layout implementation plan

Goal: Apply the approved image to the SqlAssist WPF Options window.

Scope: Preserve existing settings, save/cancel, AI Assistant work and host theme support. Remove breadcrumbs from Options tabs only. Use a 40/60 settings/preview split, compact usage strip, fixed footer, numbered syntax-colored SQL preview. Retain scrolling at small sizes.

1. Extend the standalone WPF smoke check to verify default side-by-side bounds, no breadcrumb, reactive preview and reachable footer; run it against the current layout to confirm failure.
2. Update OptionsWindow only, reusing the existing theme and formatter. Keep the minimum width supported and scroll settings independently at small heights.
3. Build Release, run core tests and standalone WPF smoke tests; inspect the rendered light layout and small window. Do not install, publish or commit as part of this request.
