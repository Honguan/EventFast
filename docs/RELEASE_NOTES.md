# EventFast v1.0.5

New:

- Active filters now have high-contrast visual states and a localized summary

Fixed:

- Quick filters now use provider-aware Event ID rules instead of matching unrelated providers
- Common Windows events have clearer classifications and more stable grouping
- Delayed first-batch callbacks can no longer overwrite completed query results or status text
