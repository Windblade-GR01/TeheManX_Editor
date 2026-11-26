using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace TeheManX_Editor.Forms;

public partial class AnimationViewerWindow : Window {
	private int enemyId;
	private int defaultTileId;
	private int defaultPaletteId;
	private int mainSpriteId;

	private readonly List<(SKBitmap bitmap, int originX, int originY)> frames = new();
	private readonly DispatcherTimer timer = new();
	private int currentFrame = 0;
	private bool isPlaying = true;
	private bool suppressEvents = false;
	private bool originCheckLocal;
	
	private static readonly SKPaint CrosshairPaint = new() {
		Color = new SKColor(0x00, 0xFF, 0x80, 0x90),
		StrokeWidth = 1,
		IsAntialias = false
	};

	public AnimationViewerWindow() {
		InitializeComponent();
	}

	public AnimationViewerWindow(int enemyId) {
		InitializeComponent();
		this.enemyId = enemyId;

		timer.Interval = TimeSpan.FromMilliseconds(1000.0 / 12.0);
		timer.Tick += Timer_Tick;

		Opened += async (_, _) => {
			int stride = (Const.Id != Const.GameId.MegaManX3) ? 2 : 5;
			int infoOffset = (this.enemyId - 1) * stride + Const.ObjectSpriteInfoOffset;
			mainSpriteId = SNES.rom[infoOffset];
			defaultTileId = SNES.rom[infoOffset + 1];
			defaultPaletteId = Const.EnemyIcons[this.enemyId].PaletteId;

			int maxCompressedTiles = Const.Id switch {
				Const.GameId.MegaManX => Const.MegaManX.CompressedTilesAmount - 1,
				Const.GameId.MegaManX2 => Const.MegaManX2.CompressedTilesAmount - 1,
				Const.GameId.MegaManX3 => Const.MegaManX3.CompressedTilesAmount - 1,
				_ => 1
			};

			suppressEvents = true;
			tileIdInt.Maximum = maxCompressedTiles;
			tileIdInt.Value = defaultTileId;
			enemyIdInt.Value = enemyId;
			palIdInt.Value = defaultPaletteId;
			suppressEvents = false;

			infoText.Text = $"Enemy: 0x{this.enemyId:X2} | Sprite: 0x{mainSpriteId:X2}";
			await ReloadFramesAsync();
		};

		Closed += (_, _) => {
			timer.Stop();
			ClearFrames();
		};
	}

	private void ClearFrames() {
		foreach (var f in frames) {
			f.bitmap?.Dispose();
		}
		frames.Clear();
	}

	private async Task ReloadFramesAsync() {
		timer.Stop();
		ClearFrames();

		int tileId = tileIdInt.Value;
		int palId = palIdInt.Value;
		int frameCount = Level.GetObjectFrameCount(mainSpriteId);

		for (int i = 0; i < frameCount; i++) {
			var frame = await Level.RenderEnemyFrameAsync(enemyId, i, tileId, palId);
			if (frame.bitmap != null) {
				frames.Add(frame);
			}
		}

		if (frames.Count > 0) {
			currentFrame = Math.Clamp(currentFrame, 0, frames.Count - 1);
			suppressEvents = true;
			frameSlider.Maximum = frames.Count - 1;
			frameSlider.Value = currentFrame;
			suppressEvents = false;
			UpdateFrameLabel();
			previewCanvas.InvalidateVisual();
			if (isPlaying)
				timer.Start();
		}
	}

	private void Timer_Tick(object? sender, EventArgs e) {
		if (!isPlaying || frames.Count <= 1)
			return;

		currentFrame = (currentFrame + 1) % frames.Count;
		SyncUI();
	}

	private void SyncUI() {
		// We sync stuff here to prevent "Call from invalid thread" error.
		// This is because things explode violently if called from RenderEvent.
		suppressEvents = true;
		frameSlider.Value = currentFrame;
		suppressEvents = false;
		UpdateFrameLabel();
		previewCanvas.InvalidateVisual();
	}

	private void UpdateFrameLabel() {
		frameLabel.Text = frames.Count == 0 ? "0 / 0" : $"{currentFrame + 1} / {frames.Count}";
	}

	private void previewCanvas_MeasureEvent(object? sender, Size e) {
		previewCanvas.MeasuredSize = e;
	}

	private void previewCanvas_RenderEvent(object? sender, SkiaCanvasEventArgs e) {
		SKCanvas canvas = e.Canvas;
		canvas.Clear(new SKColor(0x25, 0x25, 0x26));

		float centerX = (float)(e.Bounds.Width * 0.5);
		float centerY = (float)(e.Bounds.Height * 0.5);

		if (originCheckLocal) {
			canvas.DrawLine(centerX, 0, centerX, (float)e.Bounds.Height, CrosshairPaint);
			canvas.DrawLine(0, centerY, (float)e.Bounds.Width, centerY, CrosshairPaint);
		}

		if (frames.Count == 0 || currentFrame >= frames.Count)
			return;

		float zoom = (zoomCombo?.SelectedIndex ?? 2) + 1;
		var (bmp, originX, originY) = frames[currentFrame];

		canvas.Save();
		canvas.Translate(centerX, centerY);
		canvas.Scale(zoom);
		// Draw anchored to the OAM 0 position so multi-part frames stay aligned.
		canvas.DrawBitmap(bmp, -originX, -originY);
		canvas.Restore();
	}

	private async void enemyIdInt_ValueChanged(object sender, int e) {
		if (suppressEvents) { return; }
		enemyId = e;
		currentFrame = 0;

		int stride = (Const.Id != Const.GameId.MegaManX3) ? 2 : 5;
		int infoOffset = (enemyId - 1) * stride + Const.ObjectSpriteInfoOffset;
		mainSpriteId = SNES.rom[infoOffset];
		defaultTileId = SNES.rom[infoOffset + 1];
		defaultPaletteId = Const.EnemyIcons.TryGetValue(enemyId, out var icon) ? icon.PaletteId : 0;

		suppressEvents = true;
		tileIdInt.Value = defaultTileId;
		palIdInt.Value = defaultPaletteId;
		suppressEvents = false;

		infoText.Text = $"Sprite: 0x{mainSpriteId:X2}";
		await ReloadFramesAsync();
	}


	private async void OverrideInt_ValueChanged(object sender, int e) {
		if (suppressEvents) return;
		await ReloadFramesAsync();
	}

	private async void resetBtn_Click(object? sender, RoutedEventArgs e) {
		suppressEvents = true;
		tileIdInt.Value = defaultTileId;
		palIdInt.Value = defaultPaletteId;
		suppressEvents = false;
		await ReloadFramesAsync();
	}

	private void frameSlider_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e) {
		if (suppressEvents || frames.Count == 0) {
			return;
		}
		currentFrame = (int)e.NewValue;
		UpdateFrameLabel();
		previewCanvas.InvalidateVisual();
	}

	private void playBtn_Click(object? sender, RoutedEventArgs e) {
		isPlaying = !isPlaying;
		playBtn.Content = isPlaying ? "Pause" : "Play";
		if (isPlaying) timer.Start();
		else timer.Stop();
	}

	private void prevBtn_Click(object? sender, RoutedEventArgs e) {
		if (frames.Count == 0) return;
		isPlaying = false;
		timer.Stop();
		playBtn.Content = "Play";
		currentFrame = (currentFrame - 1 + frames.Count) % frames.Count;
		SyncUI();
	}

	private void nextBtn_Click(object? sender, RoutedEventArgs e) {
		if (frames.Count == 0) return;
		isPlaying = false;
		timer.Stop();
		playBtn.Content = "Play";
		currentFrame = (currentFrame + 1) % frames.Count;
		SyncUI();
	}

	private void fpsCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e) {
		if (fpsCombo?.SelectedItem is ComboBoxItem item && double.TryParse(item.Content?.ToString(), out double fps) && fps > 0) {
			timer.Interval = TimeSpan.FromMilliseconds(1000.0 / fps);
		}
	}

	private void originCheck_Enable(object? sender, RoutedEventArgs e) {
		originCheckLocal = true;
		previewCanvas?.InvalidateVisual();
	}

	private void originCheck_Disable(object? sender, RoutedEventArgs e) {
		originCheckLocal = false;
		previewCanvas?.InvalidateVisual();
	}

	private void OnRedrawOptionChanged(object? sender, RoutedEventArgs e) {
		previewCanvas?.InvalidateVisual();
	}


	private async void exportBtn_Click(object? sender, RoutedEventArgs e) {
		if (frames.Count == 0) {
			return;
		}
		IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
			Title = "Export Enemy Sprite Sheet",
			SuggestedFileName = $"Enemy_{enemyId:X2}_Sprite_{mainSpriteId:X2}.png",
			DefaultExtension = "png",
			FileTypeChoices = [
				new FilePickerFileType("PNG Image") { Patterns = ["*.png"] }
			]
		});
		if (file == null) {
			return;
		}
		// Compute uniform cell bounds anchored around OAM so frames don't jitter... that much.
		int minLeft = 0, minTop = 0, maxRight = 0, maxBottom = 0;
		foreach (var (bmp, originX, originY) in frames) {
			minLeft = Math.Min(minLeft, -originX);
			minTop = Math.Min(minTop, -originY);
			maxRight = Math.Max(maxRight, bmp.Width - originX);
			maxBottom = Math.Max(maxBottom, bmp.Height - originY);
		}

		int cellWidth = Math.Max(1, maxRight - minLeft);
		int cellHeight = Math.Max(1, maxBottom - minTop);

		using SKBitmap sheet = new SKBitmap(
			cellWidth * frames.Count, cellHeight, SKColorType.Bgra8888, SKAlphaType.Premul
		);
		using SKCanvas sheetCanvas = new SKCanvas(sheet);
		sheetCanvas.Clear(SKColors.Transparent);

		for (int i = 0; i < frames.Count; i++) {
			var (bmp, originX, originY) = frames[i];
			int drawX = (i * cellWidth) + (-minLeft - originX);
			int drawY = -minTop - originY;
			sheetCanvas.DrawBitmap(bmp, drawX, drawY);
		}

		await using Stream stream = await file.OpenWriteAsync();
		using SKData encoded = sheet.Encode(SKEncodedImageFormat.Png, 100);
		encoded.SaveTo(stream);
	}
}
