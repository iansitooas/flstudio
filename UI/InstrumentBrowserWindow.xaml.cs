using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FLLiteStudio.Audio;
using FLLiteStudio.Core;

namespace FLLiteStudio.UI;

public partial class InstrumentBrowserWindow : Window
{
    private readonly AudioEngine _audio;
    private readonly Action<ChannelModel> _onAddChannel;
    private string _selectedCategory = "Todos";
    private int _addedCount;

    public InstrumentBrowserWindow(AudioEngine audio, Action<ChannelModel> onAddChannel)
    {
        InitializeComponent();
        _audio = audio;
        _onAddChannel = onAddChannel;

        BuildCategories();
        RenderInstruments();
    }

    private static SolidColorBrush BrushFromHex(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private void BuildCategories()
    {
        CategoriesPanel.Children.Clear();
        foreach (var cat in InstrumentLibrary.Categories)
        {
            int count = cat == "Todos"
                ? InstrumentLibrary.All.Length
                : InstrumentLibrary.All.Count(p => p.Category == cat);

            bool selected = cat == _selectedCategory;
            var btn = new Button
            {
                Content = $"{cat} ({count})",
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 4),
                Background = BrushFromHex(selected ? "#C86D1F" : "#222832"),
                BorderBrush = BrushFromHex(selected ? "#F0984B" : "#313946"),
                Foreground = Brushes.White,
                FontSize = 11.5
            };
            btn.Click += (_, _) =>
            {
                _selectedCategory = cat;
                BuildCategories();
                RenderInstruments();
            };
            CategoriesPanel.Children.Add(btn);
        }
    }

    private void RenderInstruments()
    {
        InstrumentsPanel.Children.Clear();
        string query = (TxtSearch.Text ?? "").Trim();

        var matches = InstrumentLibrary.All.Where(p =>
        {
            if (_selectedCategory != "Todos" && p.Category != _selectedCategory) return false;
            if (string.IsNullOrEmpty(query)) return true;
            return p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || p.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                || p.Category.Contains(query, StringComparison.OrdinalIgnoreCase);
        }).ToList();

        LblCount.Text = $"Mostrando {matches.Count} de {InstrumentLibrary.All.Length} instrumentos — Haz clic en Probar para escuchar";

        if (matches.Count == 0)
        {
            InstrumentsPanel.Children.Add(new TextBlock
            {
                Text = "No se encontraron instrumentos con ese filtro. Prueba buscar 'guitarra', 'piano', '808', 'flauta', 'phonk' o 'pad'.",
                Foreground = BrushFromHex("#8F9DB0"), FontSize = 12, Margin = new Thickness(8)
            });
            return;
        }

        foreach (var preset in matches)
        {
            var card = new Border
            {
                Background = BrushFromHex("#1F252E"),
                BorderBrush = BrushFromHex("#303846"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var stripe = new Border
            {
                Background = BrushFromHex(preset.ColorHex),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 1, 0, 1)
            };
            Grid.SetColumn(stripe, 0);
            grid.Children.Add(stripe);

            var info = new StackPanel { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            var topLine = new StackPanel { Orientation = Orientation.Horizontal };
            topLine.Children.Add(new TextBlock
            {
                Text = preset.Name, FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = Brushes.White
            });
            topLine.Children.Add(new TextBlock
            {
                Text = $"  •  {preset.Category}", FontSize = 10.5, Foreground = BrushFromHex("#8A98AC"), VerticalAlignment = VerticalAlignment.Center
            });
            info.Children.Add(topLine);
            info.Children.Add(new TextBlock
            {
                Text = preset.Description, FontSize = 11.5, Foreground = BrushFromHex("#B4C0D0"),
                Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(info, 1);
            grid.Children.Add(info);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var previewBtn = new Button { Content = "Probar", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 0) };
            previewBtn.Click += (_, _) => PreviewPresetAsync(preset);

            var addBtn = new Button
            {
                Content = "Añadir", Style = (Style)FindResource("AccentButton"),
                Padding = new Thickness(12, 5, 12, 5)
            };
            addBtn.Click += (_, _) => AddPreset(preset);

            btns.Children.Add(previewBtn);
            btns.Children.Add(addBtn);
            Grid.SetColumn(btns, 2);
            grid.Children.Add(btns);

            card.Child = grid;
            card.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2) AddPreset(preset);
            };
            InstrumentsPanel.Children.Add(card);
        }
    }

    private async void PreviewPresetAsync(InstrumentPreset preset)
    {
        var tempCh = preset.CreateChannel();
        tempCh.MixerTrack = 0; // Preview straight through Master
        var notes = preset.PreviewNotes;
        double dur = tempCh.Type is InstrumentType.StringsPad or InstrumentType.KeysPiano or InstrumentType.PluckedString ? 0.75 : 0.40;

        for (int i = 0; i < notes.Length; i++)
        {
            _audio.PreviewNote(tempCh, notes[i], 0.85f, dur);
            if (notes.Length > 1 && i < notes.Length - 1)
                await Task.Delay(32); // Organic chord strum preview!
        }
    }

    private void AddPreset(InstrumentPreset preset)
    {
        var ch = preset.CreateChannel();
        _onAddChannel(ch);
        _addedCount++;
        LblFooterStatus.Text = $"Añadido '{ch.Name}' al proyecto ({_addedCount} añadidos en esta sesión).";
        PreviewPresetAsync(preset);
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => RenderInstruments();
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
