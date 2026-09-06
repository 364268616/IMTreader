using System.Windows.Documents;
using IMTReader.App.Controls;
using IMTReader.App.ViewModels;
using IMTReader.Core.Models;
using Xunit;

namespace IMTReader.Tests;

/// <summary>文本块编辑器（RichTextBox 封装）的文本/高亮往返与偏移映射测试。WPF 控件需要在 STA 线程上创建。</summary>
public class BlockEditorTests
{
    private static T RunSta<T>(Func<T> func)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw new Xunit.Sdk.XunitException("STA 线程异常：" + error);
        return result;
    }

    private static Highlight H(int start, int length, string color = "#FFF176") => new() { Start = start, Length = length, Color = color };

    [Fact]
    public void LoadAndExtractRoundTripsTextAndHighlights()
    {
        var (text, highlights) = RunSta(() =>
        {
            var editor = new BlockEditor();
            editor.LoadText("學而\n時習之\n不亦說乎", new[] { H(3, 2), H(7, 3, "#90CAF9") });
            return editor.Extract();
        });
        Assert.Equal("學而\n時習之\n不亦說乎", text);
        Assert.Equal(2, highlights.Count);
        Assert.Equal((3, 2, "#FFF176"), highlights[0]);
        Assert.Equal((7, 3, "#90CAF9"), highlights[1]);
    }

    [Fact]
    public void SelectRangeSelectsAcrossLineBreakOffsets()
    {
        var selected = RunSta(() =>
        {
            var editor = new BlockEditor();
            editor.LoadText("學而\n時習之", Array.Empty<Highlight>());
            editor.SelectRange(3, 2);
            return editor.SelectedText;
        });
        Assert.Equal("時習", selected);
    }

    [Fact]
    public void HighlightSelectionIsExtractedWithOffsets()
    {
        var (text, highlights) = RunSta(() =>
        {
            var editor = new BlockEditor();
            editor.LoadText("子曰學而時習之", Array.Empty<Highlight>());
            editor.SelectRange(2, 4);
            Assert.True(editor.HighlightSelection("#A5D6A7"));
            return editor.Extract();
        });
        Assert.Equal("子曰學而時習之", text);
        Assert.Single(highlights);
        Assert.Equal((2, 4, "#A5D6A7"), highlights[0]);
    }

    [Fact]
    public void EditingTextShiftsExistingHighlights()
    {
        var (text, highlights) = RunSta(() =>
        {
            var editor = new BlockEditor();
            editor.LoadText("學而時習之", new[] { H(2, 2) });
            editor.TextBox.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward).InsertTextInRun("子曰");
            return editor.Extract();
        });
        Assert.Equal("子曰學而時習之", text);
        Assert.Single(highlights);
        Assert.Equal(4, highlights[0].Start);
        Assert.Equal(2, highlights[0].Length);
    }

    [Fact]
    public void ClearHighlightsRemovesAll()
    {
        var highlights = RunSta(() =>
        {
            var editor = new BlockEditor();
            editor.LoadText("學而時習之", new[] { H(0, 2), H(3, 2) });
            editor.ClearHighlights();
            return editor.Extract().Highlights;
        });
        Assert.Empty(highlights);
    }

    [Fact]
    public void ViewModelSimplifiedModeMakesEditorReadOnly()
    {
        var (readOnly, display) = RunSta(() =>
        {
            var vm = new BlockViewModel(new OcrBlock { Id = 1, Order = 0, Text = "學而時習之", IsVertical = true }, Array.Empty<Highlight>());
            var editor = new BlockEditor { DataContext = vm };
            vm.ShowSimplified = true;
            return (editor.TextBox.IsReadOnly, editor.Extract().Text);
        });
        Assert.True(readOnly);
        Assert.Equal("学而时习之", display);
    }
}
