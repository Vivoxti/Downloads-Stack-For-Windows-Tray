using Xunit;

// Several checks in this suite measure elapsed time or allocation. Sharing the machine with another test
// class makes those numbers describe the test run rather than the code under test.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
