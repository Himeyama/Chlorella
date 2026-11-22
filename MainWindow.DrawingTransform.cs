using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace Chlorella;

public sealed partial class MainWindow
{
    async Task EditObjectRotationAsync(string kind, uint id, double rotation)
    {
        NumberBox angle = new() { Header = "角度（度）", Value = rotation, SmallChange = 1, LargeChange = 15,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        TextBlock error = new() { TextWrapping = TextWrapping.Wrap };
        StackPanel panel = new() { Spacing = 8 };
        panel.Children.Add(angle);
        panel.Children.Add(error);
        ContentDialog dialog = new() { XamlRoot = RootGrid.XamlRoot, Title = "回転", Content = panel,
            PrimaryButtonText = "適用", CloseButtonText = "キャンセル" };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (double.IsFinite(angle.Value)) return;
            error.Text = "角度を入力してください。";
            args.Cancel = true;
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await CommitInputAsync();
        await ExecuteAsync("object.transform", new { kind, id, rotation = angle.Value });
        dirty = true;
        await RefreshAsync();
        FocusObject(kind, id);
    }

    async Task EditImageCropAsync(uint id, JsonElement picture, BitmapImage? bitmap)
    {
        StackPanel panel = new() { Spacing = 8 };
        JsonElement anchor = picture.GetProperty("anchor");
        double width = anchor.GetProperty("width").GetDouble(), height = anchor.GetProperty("height").GetDouble();
        double scale = Math.Min(260 / width, 180 / height);
        double previewWidth = width * scale, previewHeight = height * scale;
        Canvas preview = new() { Width = previewWidth, Height = previewHeight, HorizontalAlignment = HorizontalAlignment.Left,
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, previewWidth, previewHeight) } };
        Image previewImage = new() { Source = bitmap, Stretch = Stretch.Fill };
        preview.Children.Add(previewImage);
        panel.Children.Add(preview);
        string[] labels = ["左（%）", "上（%）", "右（%）", "下（%）"];
        string[] properties = ["cropLeft", "cropTop", "cropRight", "cropBottom"];
        NumberBox[] inputs = [new(), new(), new(), new()];
        void UpdatePreview()
        {
            double left = inputs[0].Value / 100, top = inputs[1].Value / 100,
                right = inputs[2].Value / 100, bottom = inputs[3].Value / 100;
            if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(right) || !double.IsFinite(bottom) ||
                left < 0 || top < 0 || right < 0 || bottom < 0 || left + right >= 1 || top + bottom >= 1) return;
            previewImage.Width = previewWidth / (1 - left - right);
            previewImage.Height = previewHeight / (1 - top - bottom);
            Canvas.SetLeft(previewImage, -left * previewImage.Width);
            Canvas.SetTop(previewImage, -top * previewImage.Height);
        }
        for (int i = 0; i < inputs.Length; i++)
        {
            inputs[i].Header = labels[i];
            inputs[i].Value = picture.GetProperty(properties[i]).GetDouble() * 100;
            inputs[i].Minimum = 0;
            inputs[i].Maximum = 99.999;
            inputs[i].SmallChange = 1;
            inputs[i].SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact;
            panel.Children.Add(inputs[i]);
        }
        foreach (NumberBox input in inputs) input.ValueChanged += (_, _) => UpdatePreview();
        UpdatePreview();
        TextBlock error = new() { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(error);
        ScrollViewer scroll = new() { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        ContentDialog dialog = new() { XamlRoot = RootGrid.XamlRoot, Title = "画像のトリミング", Content = scroll,
            PrimaryButtonText = "適用", CloseButtonText = "キャンセル" };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            double[] current = inputs.Select(input => input.Value / 100).ToArray();
            if (current.All(double.IsFinite) && current.All(value => value >= 0) &&
                current[0] + current[2] < 1 && current[1] + current[3] < 1) return;
            error.Text = "左右と上下の合計はそれぞれ 100% 未満にしてください。";
            args.Cancel = true;
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        double[] values = inputs.Select(input => input.Value / 100).ToArray();
        await CommitInputAsync();
        await ExecuteAsync("image.crop", new { kind = "image", id, left = values[0], top = values[1], right = values[2], bottom = values[3] });
        dirty = true;
        await RefreshAsync();
        FocusObject("image", id);
    }
}
