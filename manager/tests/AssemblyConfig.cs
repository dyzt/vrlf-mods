using Xunit;

// Several tests capture the process-global Console.Out (dispatch/--json + the TUI
// non-interactive fallback). xUnit parallelizes across test classes by default, so those
// captures would race and cross-contaminate. The whole suite runs in well under a second,
// so serialize it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
