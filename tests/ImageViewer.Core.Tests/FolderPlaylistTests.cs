using ImageViewer.Core.Navigation;

namespace ImageViewer.Core.Tests;

public class FolderPlaylistTests
{
    [Fact]
    public async Task 打开图片后自动扫描同目录混合格式并定位当前图片()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ImageViewer-playlist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            foreach (var name in new[] { "图10.jpg", "图2.PNG", "图1.gif", "notes.txt", "no-extension" })
            {
                File.WriteAllText(Path.Combine(folder, name), string.Empty);
            }
            var nested = Directory.CreateDirectory(Path.Combine(folder, "nested"));
            File.WriteAllText(Path.Combine(nested.FullName, "nested.png"), string.Empty);

            var playlist = await FolderPlaylist.CreateAsync(Path.Combine(folder, "图2.PNG"), CancellationToken.None);

            Assert.Equal(folder, playlist.FolderPath);
            Assert.Equal(new[] { "图1.gif", "图2.PNG", "图10.jpg" }, playlist.Files.Select(Path.GetFileName));
            Assert.Equal(1, playlist.CurrentIndex);
            Assert.True(playlist.MoveNext());
            Assert.EndsWith("图10.jpg", playlist.CurrentPath);
            Assert.True(playlist.IsLast);
            Assert.True(playlist.MovePrevious());
            Assert.EndsWith("图2.PNG", playlist.CurrentPath);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static FolderPlaylist Build(params string[] names) =>
        FolderPlaylist.FromPaths(names.Select(n => $@"C:\photos\{n}"));

    // ---------- 自然排序：必须和资源管理器一致 ----------

    [Fact]
    public void 数字按数值大小排序而不是按字符串排序()
    {
        var playlist = Build("img10.png", "img2.png", "img1.png");

        var names = playlist.Files.Select(Path.GetFileName).ToArray();

        Assert.Equal(new[] { "img1.png", "img2.png", "img10.png" }, names);
    }

    [Fact]
    public void 多段数字也按自然顺序排列()
    {
        var playlist = Build("IMG_100.jpg", "IMG_9.jpg", "IMG_20.jpg");

        var names = playlist.Files.Select(Path.GetFileName).ToArray();

        Assert.Equal(new[] { "IMG_9.jpg", "IMG_20.jpg", "IMG_100.jpg" }, names);
    }

    // ---------- 翻页 ----------

    [Fact]
    public void 翻到下一张与上一张()
    {
        var playlist = Build("a.png", "b.png", "c.png");

        Assert.True(playlist.MoveNext());
        Assert.Equal(@"C:\photos\b.png", playlist.CurrentPath);

        Assert.True(playlist.MovePrevious());
        Assert.Equal(@"C:\photos\a.png", playlist.CurrentPath);
    }

    [Fact]
    public void 到达末尾后不再前进且不回绕()
    {
        var playlist = Build("a.png", "b.png");
        playlist.MoveLast();

        Assert.True(playlist.IsLast);
        Assert.False(playlist.MoveNext());
        Assert.Equal(@"C:\photos\b.png", playlist.CurrentPath);
    }

    [Fact]
    public void 到达开头后不再后退且不回绕()
    {
        var playlist = Build("a.png", "b.png");

        Assert.True(playlist.IsFirst);
        Assert.False(playlist.MovePrevious());
        Assert.Equal(@"C:\photos\a.png", playlist.CurrentPath);
    }

    [Fact]
    public void 可以按路径跳转()
    {
        var playlist = Build("a.png", "b.png", "c.png");

        Assert.True(playlist.MoveTo(@"C:\photos\c.png"));
        Assert.Equal(2, playlist.CurrentIndex);

        Assert.False(playlist.MoveTo(@"C:\photos\不存在.png"));
    }

    [Fact]
    public void 按路径跳转时忽略大小写()
    {
        var playlist = Build("a.png", "b.png");

        Assert.True(playlist.MoveTo(@"C:\PHOTOS\B.PNG"));
        Assert.Equal(1, playlist.CurrentIndex);
    }

    // ---------- 预读顺序 ----------

    [Fact]
    public void 预读候选优先给下一张()
    {
        var playlist = Build("1.png", "2.png", "3.png", "4.png", "5.png");
        playlist.MoveTo(2); // 当前是 3.png

        var candidates = playlist.GetPrefetchCandidates(2).Select(Path.GetFileName).ToArray();

        // 逐圈向外扩，每圈先下一张再上一张
        Assert.Equal(new[] { "4.png", "2.png", "5.png", "1.png" }, candidates);
    }

    [Fact]
    public void 预读候选不会越过列表边界()
    {
        var playlist = Build("1.png", "2.png");

        var candidates = playlist.GetPrefetchCandidates(5).Select(Path.GetFileName).ToArray();

        Assert.Equal(new[] { "2.png" }, candidates);
    }

    // ---------- 删除文件后的位置调整 ----------

    [Fact]
    public void 删除当前图后停在原位置上的下一张()
    {
        var playlist = Build("a.png", "b.png", "c.png");
        playlist.MoveTo(1);

        Assert.True(playlist.Remove(@"C:\photos\b.png"));

        Assert.Equal(1, playlist.CurrentIndex);
        Assert.Equal(@"C:\photos\c.png", playlist.CurrentPath);
    }

    [Fact]
    public void 删除最后一张后退回新的末尾()
    {
        var playlist = Build("a.png", "b.png");
        playlist.MoveLast();

        playlist.Remove(@"C:\photos\b.png");

        Assert.Equal(0, playlist.CurrentIndex);
        Assert.Equal(@"C:\photos\a.png", playlist.CurrentPath);
    }

    [Fact]
    public void 删除当前之前的图时当前位置跟着前移()
    {
        var playlist = Build("a.png", "b.png", "c.png");
        playlist.MoveTo(2);

        playlist.Remove(@"C:\photos\a.png");

        Assert.Equal(1, playlist.CurrentIndex);
        Assert.Equal(@"C:\photos\c.png", playlist.CurrentPath);
    }

    [Fact]
    public void 删光之后列表为空()
    {
        var playlist = Build("a.png");

        playlist.Remove(@"C:\photos\a.png");

        Assert.True(playlist.IsEmpty);
        Assert.Null(playlist.CurrentPath);
    }
}
