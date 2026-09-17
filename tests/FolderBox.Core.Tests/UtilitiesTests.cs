using FolderBox.Core.Services;
using FolderBox.Core.Utilities;
using Xunit;

namespace FolderBox.Core.Tests;

public class UtilitiesTests
{
    [Fact]
    public async Task Debouncer_coalesces_bursts()
    {
        int calls = 0;
        using var d = new Debouncer(TimeSpan.FromMilliseconds(80), () => Interlocked.Increment(ref calls));
        for (int i = 0; i < 20; i++) { d.Trigger(); await Task.Delay(5); }
        await Task.Delay(300);
        Assert.Equal(1, calls);

        d.Trigger();
        await Task.Delay(300);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Debouncer_cancel_prevents_callback()
    {
        int calls = 0;
        using var d = new Debouncer(TimeSpan.FromMilliseconds(50), () => Interlocked.Increment(ref calls));
        d.Trigger();
        d.Cancel();
        await Task.Delay(200);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(@"D:\Projects\SunsetSettlement", "SunsetSettlement")]
    [InlineData(@"D:\Projects\SunsetSettlement\", "SunsetSettlement")]
    [InlineData(@"D:\", @"D:\")]
    [InlineData(@"D:", @"D:\")]
    [InlineData(@"\\server\share\Docs", "Docs")]
    public void Display_name_from_path(string path, string expected)
    {
        Assert.Equal(expected, PathHelpers.GetDisplayNameForFolder(path));
    }

    [Fact]
    public void Sub_path_detection()
    {
        Assert.True(PathHelpers.IsSubPathOf(@"C:\A\B\C", @"C:\A"));
        Assert.True(PathHelpers.IsSubPathOf(@"C:\A", @"C:\A"));
        Assert.False(PathHelpers.IsSubPathOf(@"C:\AB", @"C:\A"));
        Assert.False(PathHelpers.IsSubPathOf(@"C:\A", @"C:\A\B"));
    }

    [Fact]
    public void Same_volume_detection()
    {
        Assert.True(PathHelpers.IsSameVolume(@"C:\x\y", @"C:\z"));
        Assert.False(PathHelpers.IsSameVolume(@"C:\x", @"D:\x"));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1,0 KB")]
    [InlineData(1536, "1,5 KB")]
    [InlineData(3_500_000, "3,3 MB")]
    [InlineData(150L * 1024 * 1024, "150 MB")]
    public void File_size_formatting(long bytes, string expected)
    {
        var formatted = FileSizeFormatter.Format(bytes);
        // Decimal separator depends on culture; compare digits only.
        Assert.Equal(expected.Replace(',', '.'), formatted.Replace(',', '.'));
    }

    [Fact]
    public void Natural_sort_orders_numbers_numerically()
    {
        var names = new[] { "file10.txt", "file2.txt", "File1.txt", "file02.txt", "b", "a" };
        var sorted = names.OrderBy(n => n, NaturalStringComparer.Instance).ToArray();
        Assert.Equal(new[] { "a", "b", "File1.txt", "file2.txt", "file02.txt", "file10.txt" }, sorted);
    }

    [Fact]
    public async Task Folder_service_lists_directories_first_and_reports_missing_folder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FolderBoxTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "zeta"));
        Directory.CreateDirectory(Path.Combine(dir, "alpha"));
        File.WriteAllText(Path.Combine(dir, "a.txt"), "1");
        File.WriteAllText(Path.Combine(dir, "B.txt"), "22");
        try
        {
            var listing = await FolderService.ListAsync(dir, CancellationToken.None);
            Assert.Equal(FolderStatus.Available, listing.Status);
            Assert.Equal(new[] { "alpha", "zeta", "a.txt", "B.txt" }, listing.Items.Select(i => i.Name).ToArray());
            Assert.Equal(2, listing.Items.Single(i => i.Name == "B.txt").Size);

            var missing = await FolderService.ListAsync(Path.Combine(dir, "nope"), CancellationToken.None);
            Assert.Equal(FolderStatus.NotFound, missing.Status);
            Assert.Equal(FolderStatus.NotFound, FolderService.GetStatus(Path.Combine(dir, "nope")));
            Assert.Equal(4, await FolderService.CountAsync(dir, 100, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Watcher_reports_external_changes_once_per_burst()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FolderBoxTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            int events = 0;
            using var watcher = new FolderWatcherService(TimeSpan.FromMilliseconds(100));
            watcher.Changed += () => Interlocked.Increment(ref events);
            Assert.True(watcher.Watch(dir));

            for (int i = 0; i < 5; i++) File.WriteAllText(Path.Combine(dir, $"f{i}.txt"), "x");
            await Task.Delay(600);
            Assert.Equal(1, events);

            File.Delete(Path.Combine(dir, "f0.txt"));
            await Task.Delay(600);
            Assert.Equal(2, events);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
