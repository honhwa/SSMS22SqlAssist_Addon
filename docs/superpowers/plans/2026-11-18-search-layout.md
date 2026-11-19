# SqlAssist Search appearance implementation plan

Goal: Apply the approved Options visual language to the existing Search tool window.

Scope: Keep search, menus, usages, navigation and SQL previews working. Use existing host theme resources, consistent spacing and bordered sections. A full-width query row precedes labeled wrapping filters. Results and SQL preview share a resizable split: side by side when wide, stacked when narrow. Status and keyboard help stay below the content.

1. Add a standalone WPF smoke check for labeled search sections, narrow/wide bounds, theme changes, result selection and preview content. Confirm failure against the old layout.
2. Adjust SearchControl layout, reuse the SettingsTheme resource dictionary, and preserve existing handlers. Add concise section headers and an object name in the preview header.
3. Build Release, run Core and WPF smoke suites, and inspect a standalone screenshot. Keep the project version unchanged. Refresh the local installation under the prior testing authorization once SSMS is closed.


User correction: restore the original single-row toolbar, top status, fixed-column result grid and bottom SQL preview. Keep only the Options-style dropdown and button appearance. Standalone WPF checks now verify the restored layout and unchanged grid/preview styling.
