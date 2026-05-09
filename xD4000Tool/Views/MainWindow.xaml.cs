using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using xD4000Tool.ViewModels;

namespace xD4000Tool.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm)
            vm.SelectedNode = e.NewValue as TreeNode;
    }

    private void TrendCanvas_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Canvas canvas)
        {
            canvas.DataContextChanged += (_, __) => DrawSeries(canvas);
            CompositionTarget.Rendering += (_, __) => DrawSeries(canvas);
            DrawSeries(canvas);
        }
    }

    private static void DrawSeries(Canvas canvas)
    {
        if (canvas.Parent is not GroupBox gb) return;
        if (gb.DataContext is not TrendSeries series) return;

        var pts = series.Points.ToList();
        if (pts.Count < 2) return;

        double w = canvas.ActualWidth; if (w <= 10) w = 800;
        double h = canvas.ActualHeight; if (h <= 10) h = 120;

        double min = pts.Min();
        double max = pts.Max();
        if (Math.Abs(max - min) < 1e-9) max = min + 1;

        canvas.Children.Clear();
        var poly = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xD6)), StrokeThickness = 2 };
        int n = pts.Count;
        for (int i = 0; i < n; i++)
        {
            double x = (i / (double)(n - 1)) * (w - 10) + 5;
            double yNorm = (pts[i] - min) / (max - min);
            double y = (1 - yNorm) * (h - 10) + 5;
            poly.Points.Add(new Point(x, y));
        }
        canvas.Children.Add(poly);
    }
}
