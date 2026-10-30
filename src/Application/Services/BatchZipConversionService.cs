using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZPL2PDF.Shared.Constants;

namespace ZPL2PDF.Application.Services
{
    /// <summary>
    /// Origin type of a ZPL item in the conversion queue.
    /// </summary>
    public enum ZplSourceType
    {
        File,
        FolderFile,
        ZipEntry,
        PastedText
    }

    /// <summary>
    /// Output mode for batch conversion.
    /// </summary>
    public enum BatchOutputMode
    {
        SeparatePdfPerFile,
        MergedSinglePdf
    }

    /// <summary>
    /// Represents a single ZPL entry in the visual application queue.
    /// </summary>
    public class ZplQueueItem
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string DisplayName { get; set; } = string.Empty;
        public string BaseFileName { get; set; } = string.Empty;
        public ZplSourceType SourceType { get; set; }
        public string SourceDescription { get; set; } = string.Empty;
        public string ZplContent { get; set; } = string.Empty;
        public int LabelCount { get; set; }
        public string Status { get; set; } = "Pronto";
        public bool IsSuccess { get; set; }
        public bool HasError { get; set; }
        public string? OutputPdfPath { get; set; }
    }

    /// <summary>
    /// Progress report during batch conversion.
    /// </summary>
    public class BatchConversionProgress
    {
        public int CurrentIndex { get; set; }
        public int TotalItems { get; set; }
        public string CurrentItemName { get; set; } = string.Empty;
        public string StatusMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Final summary of a batch conversion operation.
    /// </summary>
    public class BatchConversionSummary
    {
        public int TotalItems { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public int TotalPagesGenerated { get; set; }
        public List<string> GeneratedPdfPaths { get; } = new List<string>();
        public string? GeneratedZipPath { get; set; }
        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>
    /// Service that handles loading ZPLs from folders, files, and .ZIP archives,
    /// rendering previews, and converting batches to individual or merged PDFs.
    /// </summary>
    public class BatchZipConversionService
    {
        private readonly ConversionService _conversionService;

        public BatchZipConversionService()
        {
            _conversionService = new ConversionService();
        }

        /// <summary>
        /// Loads ZPL items from any combination of files, folders, and .zip archives.
        /// </summary>
        public List<ZplQueueItem> LoadFromPaths(
            IEnumerable<string> paths,
            bool includeSubfolders = true,
            bool detectContentWithoutZplExtension = true)
        {
            var items = new List<ZplQueueItem>();

            foreach (var rawPath in paths)
            {
                if (string.IsNullOrWhiteSpace(rawPath))
                    continue;

                var path = rawPath.Trim();

                if (Directory.Exists(path))
                {
                    LoadFromDirectory(path, includeSubfolders, detectContentWithoutZplExtension, items);
                }
                else if (File.Exists(path))
                {
                    if (IsZipFile(path))
                    {
                        LoadFromZipFile(path, detectContentWithoutZplExtension, items);
                    }
                    else
                    {
                        var item = TryCreateItemFromFile(path, ZplSourceType.File, path, detectContentWithoutZplExtension: true);
                        if (item != null)
                        {
                            items.Add(item);
                        }
                    }
                }
            }

            return items;
        }

        /// <summary>
        /// Creates a queue item from pasted ZPL text.
        /// </summary>
        public ZplQueueItem? CreateFromText(string zplContent, string? customName = null)
        {
            if (string.IsNullOrWhiteSpace(zplContent))
                return null;

            int labelCount = CountLabels(zplContent);
            if (labelCount == 0 && !ContainsZplMarkers(zplContent))
                return null;

            string baseName = string.IsNullOrWhiteSpace(customName)
                ? $"ZPL_Manual_{DateTime.Now:HHmmss}"
                : Path.GetFileNameWithoutExtension(customName.Trim());

            return new ZplQueueItem
            {
                DisplayName = $"{baseName}.zpl",
                BaseFileName = baseName,
                SourceType = ZplSourceType.PastedText,
                SourceDescription = "Texto colado / Editor",
                ZplContent = zplContent,
                LabelCount = Math.Max(1, labelCount),
                Status = "Pronto"
            };
        }

        /// <summary>
        /// Counts how many labels (^XA...^XZ) exist in the ZPL string.
        /// </summary>
        public static int CountLabels(string zplContent)
        {
            if (string.IsNullOrWhiteSpace(zplContent))
                return 0;

            try
            {
                var preprocessed = LabelFileReader.PreprocessZpl(zplContent);
                return LabelFileReader.SplitLabels(preprocessed).Count;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Generates PNG preview images (one byte[] per label page) for a ZPL content string.
        /// </summary>
        public List<byte[]> RenderPreviewImages(
            string zplContent,
            double width,
            double height,
            string unit,
            int dpi,
            RendererEngine rendererEngine)
        {
            if (string.IsNullOrWhiteSpace(zplContent))
                return new List<byte[]>();

            return _conversionService.Convert(
                zplContent,
                width,
                height,
                unit,
                dpi,
                rendererEngine: rendererEngine);
        }

        /// <summary>
        /// Converts a list of queue items into PDFs according to the user's chosen options.
        /// </summary>
        public async Task<BatchConversionSummary> ConvertBatchAsync(
            IReadOnlyList<ZplQueueItem> items,
            string outputFolder,
            BatchOutputMode outputMode,
            string? mergedFileName,
            bool alsoCreateOutputZip,
            double width,
            double height,
            string unit,
            int dpi,
            RendererEngine rendererEngine,
            IProgress<BatchConversionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var summary = new BatchConversionSummary
            {
                TotalItems = items.Count
            };

            if (items.Count == 0)
                return summary;

            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            await Task.Run(() =>
            {
                if (outputMode == BatchOutputMode.SeparatePdfPerFile)
                {
                    ConvertSeparateFiles(
                        items,
                        outputFolder,
                        width,
                        height,
                        unit,
                        dpi,
                        rendererEngine,
                        progress,
                        summary,
                        cancellationToken);
                }
                else
                {
                    ConvertMergedFile(
                        items,
                        outputFolder,
                        mergedFileName,
                        width,
                        height,
                        unit,
                        dpi,
                        rendererEngine,
                        progress,
                        summary,
                        cancellationToken);
                }

                if (alsoCreateOutputZip && summary.GeneratedPdfPaths.Count > 0 && !cancellationToken.IsCancellationRequested)
                {
                    string zipFileName = $"PDFs_ZPL_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
                    string zipPath = EnsureUniqueFilePath(Path.Combine(outputFolder, zipFileName));

                    using (var zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
                    using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
                    {
                        foreach (var pdfPath in summary.GeneratedPdfPaths)
                        {
                            if (File.Exists(pdfPath))
                            {
                                archive.CreateEntryFromFile(pdfPath, Path.GetFileName(pdfPath), CompressionLevel.Fastest);
                            }
                        }
                    }

                    summary.GeneratedZipPath = zipPath;
                }
            }, cancellationToken);

            return summary;
        }

        private void ConvertSeparateFiles(
            IReadOnlyList<ZplQueueItem> items,
            string outputFolder,
            double width,
            double height,
            string unit,
            int dpi,
            RendererEngine rendererEngine,
            IProgress<BatchConversionProgress>? progress,
            BatchConversionSummary summary,
            CancellationToken cancellationToken)
        {
            var usedFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < items.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var item = items[i];
                item.Status = "Convertendo...";
                item.HasError = false;
                item.IsSuccess = false;

                progress?.Report(new BatchConversionProgress
                {
                    CurrentIndex = i + 1,
                    TotalItems = items.Count,
                    CurrentItemName = item.DisplayName,
                    StatusMessage = $"Convertendo ({i + 1}/{items.Count}): {item.DisplayName}"
                });

                try
                {
                    string safeBaseName = SanitizeFileName(item.BaseFileName);
                    if (string.IsNullOrWhiteSpace(safeBaseName))
                    {
                        safeBaseName = $"Etiqueta_{i + 1}";
                    }

                    string desiredPath = Path.Combine(outputFolder, $"{safeBaseName}.pdf");
                    string finalPath = EnsureUniqueFilePath(desiredPath, usedFilePaths);
                    usedFilePaths.Add(finalPath);

                    int pagesGenerated = ConvertSingleZplToPdfFile(
                        item.ZplContent,
                        finalPath,
                        width,
                        height,
                        unit,
                        dpi,
                        rendererEngine);

                    item.OutputPdfPath = finalPath;
                    item.IsSuccess = true;
                    item.Status = $"Concluído ({pagesGenerated} pág.)";
                    summary.SuccessCount++;
                    summary.TotalPagesGenerated += pagesGenerated;
                    summary.GeneratedPdfPaths.Add(finalPath);
                }
                catch (Exception ex)
                {
                    item.HasError = true;
                    item.Status = $"Erro: {ex.Message}";
                    summary.ErrorCount++;
                    summary.Errors.Add($"{item.DisplayName}: {ex.Message}");
                }
            }
        }

        private void ConvertMergedFile(
            IReadOnlyList<ZplQueueItem> items,
            string outputFolder,
            string? mergedFileName,
            double width,
            double height,
            string unit,
            int dpi,
            RendererEngine rendererEngine,
            IProgress<BatchConversionProgress>? progress,
            BatchConversionSummary summary,
            CancellationToken cancellationToken)
        {
            var allPdfParts = new List<byte[]>();
            var allImages = new List<byte[]>();
            bool canUseDirectPdfMerge = rendererEngine == RendererEngine.Labelary || rendererEngine == RendererEngine.Auto;

            string baseName = string.IsNullOrWhiteSpace(mergedFileName)
                ? $"Etiquetas_Unificadas_{DateTime.Now:yyyyMMdd_HHmmss}"
                : Path.GetFileNameWithoutExtension(mergedFileName.Trim());

            string safeBaseName = SanitizeFileName(baseName);
            string finalPath = EnsureUniqueFilePath(Path.Combine(outputFolder, $"{safeBaseName}.pdf"));

            for (int i = 0; i < items.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var item = items[i];
                item.Status = "Processando...";
                item.HasError = false;
                item.IsSuccess = false;

                progress?.Report(new BatchConversionProgress
                {
                    CurrentIndex = i + 1,
                    TotalItems = items.Count,
                    CurrentItemName = item.DisplayName,
                    StatusMessage = $"Processando ({i + 1}/{items.Count}): {item.DisplayName}"
                });

                try
                {
                    if (canUseDirectPdfMerge &&
                        _conversionService.TryConvertPdfDirectWithLabelary(
                            item.ZplContent,
                            width,
                            height,
                            unit,
                            dpi,
                            rendererEngine,
                            out var directPdfBytes) &&
                        directPdfBytes != null &&
                        directPdfBytes.Length > 0)
                    {
                        allPdfParts.Add(directPdfBytes);
                        int pageCount = Math.Max(1, CountLabels(item.ZplContent));
                        item.IsSuccess = true;
                        item.OutputPdfPath = finalPath;
                        item.Status = $"Incluído ({pageCount} pág.)";
                        summary.SuccessCount++;
                        summary.TotalPagesGenerated += pageCount;
                        continue;
                    }

                    // Offline or fallback to PNG rendering
                    canUseDirectPdfMerge = false;
                    var images = _conversionService.Convert(
                        item.ZplContent,
                        width,
                        height,
                        unit,
                        dpi,
                        rendererEngine: rendererEngine);

                    if (images == null || images.Count == 0)
                    {
                        throw new InvalidOperationException("Nenhuma etiqueta válida gerada a partir deste ZPL.");
                    }

                    allImages.AddRange(images);
                    item.IsSuccess = true;
                    item.OutputPdfPath = finalPath;
                    item.Status = $"Incluído ({images.Count} pág.)";
                    summary.SuccessCount++;
                    summary.TotalPagesGenerated += images.Count;
                }
                catch (Exception ex)
                {
                    item.HasError = true;
                    item.Status = $"Erro: {ex.Message}";
                    summary.ErrorCount++;
                    summary.Errors.Add($"{item.DisplayName}: {ex.Message}");
                }
            }

            if (summary.SuccessCount > 0)
            {
                progress?.Report(new BatchConversionProgress
                {
                    CurrentIndex = items.Count,
                    TotalItems = items.Count,
                    CurrentItemName = Path.GetFileName(finalPath),
                    StatusMessage = "Salvando PDF consolidado..."
                });

                if (allPdfParts.Count > 0 && allImages.Count == 0)
                {
                    byte[] mergedBytes = allPdfParts.Count == 1
                        ? allPdfParts[0]
                        : PdfGenerator.MergePdfsToBytes(allPdfParts);
                    File.WriteAllBytes(finalPath, mergedBytes);
                }
                else if (allPdfParts.Count > 0 && allImages.Count > 0)
                {
                    byte[] imagePdfBytes = PdfGenerator.GeneratePdfToBytes(allImages);
                    allPdfParts.Add(imagePdfBytes);
                    byte[] mergedBytes = PdfGenerator.MergePdfsToBytes(allPdfParts);
                    File.WriteAllBytes(finalPath, mergedBytes);
                }
                else
                {
                    PdfGenerator.GeneratePdf(allImages, finalPath);
                }

                summary.GeneratedPdfPaths.Add(finalPath);
            }
        }

        public int ConvertSingleZplToPdfFile(
            string zplContent,
            string outputPdfPath,
            double width,
            double height,
            string unit,
            int dpi,
            RendererEngine rendererEngine)
        {
            if (_conversionService.TryConvertPdfDirectWithLabelary(
                zplContent,
                width,
                height,
                unit,
                dpi,
                rendererEngine,
                out var directPdfBytes) &&
                directPdfBytes != null &&
                directPdfBytes.Length > 0)
            {
                File.WriteAllBytes(outputPdfPath, directPdfBytes);
                return Math.Max(1, CountLabels(zplContent));
            }

            var images = _conversionService.Convert(
                zplContent,
                width,
                height,
                unit,
                dpi,
                rendererEngine: rendererEngine);

            if (images == null || images.Count == 0)
            {
                throw new InvalidOperationException("Nenhuma etiqueta encontrada ou gerada para o arquivo.");
            }

            PdfGenerator.GeneratePdf(images, outputPdfPath);
            return images.Count;
        }

        private void LoadFromDirectory(
            string directoryPath,
            bool includeSubfolders,
            bool detectContentWithoutZplExtension,
            List<ZplQueueItem> targetList)
        {
            var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            IEnumerable<string> files;

            try
            {
                files = Directory.EnumerateFiles(directoryPath, "*.*", searchOption);
            }
            catch
            {
                return;
            }

            foreach (var file in files)
            {
                if (IsZipFile(file))
                {
                    LoadFromZipFile(file, detectContentWithoutZplExtension, targetList);
                }
                else
                {
                    var item = TryCreateItemFromFile(file, ZplSourceType.FolderFile, directoryPath, detectContentWithoutZplExtension);
                    if (item != null)
                    {
                        targetList.Add(item);
                    }
                }
            }
        }

        private void LoadFromZipFile(
            string zipFilePath,
            bool detectContentWithoutZplExtension,
            List<ZplQueueItem> targetList)
        {
            try
            {
                using var fileStream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                LoadFromZipStream(fileStream, Path.GetFileName(zipFilePath), detectContentWithoutZplExtension, targetList, 0);
            }
            catch
            {
                // Ignore corrupted/unreadable zip files gracefully
            }
        }

        private void LoadFromZipStream(
            Stream stream,
            string zipDisplayName,
            bool detectContentWithoutZplExtension,
            List<ZplQueueItem> targetList,
            int depth)
        {
            if (depth > 3)
                return;

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            foreach (var entry in archive.Entries)
            {
                // Skip directories
                if (string.IsNullOrEmpty(entry.Name) || entry.Length == 0)
                    continue;

                // Support nested .zip inside .zip!
                if (IsZipFile(entry.Name))
                {
                    try
                    {
                        using var nestedStream = entry.Open();
                        using var ms = new MemoryStream();
                        nestedStream.CopyTo(ms);
                        ms.Position = 0;
                        LoadFromZipStream(ms, $"{zipDisplayName}/{entry.Name}", detectContentWithoutZplExtension, targetList, depth + 1);
                    }
                    catch
                    {
                        // Ignore unreadable nested zip
                    }
                    continue;
                }

                // Avoid huge binary files (> 25 MB)
                if (entry.Length > 25 * 1024 * 1024)
                    continue;

                bool hasStandardExtension = IsStandardZplExtension(entry.Name);
                if (!hasStandardExtension && !detectContentWithoutZplExtension)
                    continue;

                if (IsKnownBinaryExtension(entry.Name))
                    continue;

                try
                {
                    using var entryStream = entry.Open();
                    using var reader = new StreamReader(entryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    string content = reader.ReadToEnd();

                    if (!ContainsZplMarkers(content))
                        continue;

                    int labelCount = CountLabels(content);
                    if (labelCount <= 0)
                        continue;

                    string entryBaseName = Path.GetFileNameWithoutExtension(entry.Name);
                    string zipBaseName = Path.GetFileNameWithoutExtension(zipDisplayName);

                    targetList.Add(new ZplQueueItem
                    {
                        DisplayName = $"{zipDisplayName} ▸ {entry.FullName}",
                        BaseFileName = archive.Entries.Count(e => !string.IsNullOrEmpty(e.Name)) == 1
                            ? zipBaseName
                            : $"{zipBaseName}_{entryBaseName}",
                        SourceType = ZplSourceType.ZipEntry,
                        SourceDescription = $"ZIP: {zipDisplayName}",
                        ZplContent = content,
                        LabelCount = labelCount,
                        Status = "Pronto"
                    });
                }
                catch
                {
                    // Skip unreadable entry
                }
            }
        }

        private ZplQueueItem? TryCreateItemFromFile(
            string filePath,
            ZplSourceType sourceType,
            string sourceOrigin,
            bool detectContentWithoutZplExtension)
        {
            try
            {
                if (IsKnownBinaryExtension(filePath))
                    return null;

                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists || fileInfo.Length == 0 || fileInfo.Length > 25 * 1024 * 1024)
                    return null;

                bool hasStandardExt = IsStandardZplExtension(filePath);
                if (!hasStandardExt && !detectContentWithoutZplExtension)
                    return null;

                string content = File.ReadAllText(filePath, Encoding.UTF8);
                if (!ContainsZplMarkers(content))
                    return null;

                int labelCount = CountLabels(content);
                if (labelCount <= 0)
                    return null;

                return new ZplQueueItem
                {
                    DisplayName = Path.GetFileName(filePath),
                    BaseFileName = Path.GetFileNameWithoutExtension(filePath),
                    SourceType = sourceType,
                    SourceDescription = sourceType == ZplSourceType.FolderFile
                        ? $"Pasta: {Path.GetFileName(sourceOrigin.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}"
                        : Path.GetDirectoryName(filePath) ?? "Arquivo",
                    ZplContent = content,
                    LabelCount = labelCount,
                    Status = "Pronto"
                };
            }
            catch
            {
                return null;
            }
        }

        public static bool IsZipFile(string path)
        {
            return string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsStandardZplExtension(string path)
        {
            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext))
                return false;

            return ApplicationConstants.VALID_FILE_EXTENSIONS.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsKnownBinaryExtension(string path)
        {
            string ext = Path.GetExtension(path)?.ToLowerInvariant() ?? string.Empty;
            return ext is ".pdf" or ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico"
                or ".exe" or ".dll" or ".pdb" or ".7z" or ".rar" or ".tar" or ".gz"
                or ".mp3" or ".mp4" or ".avi" or ".mov" or ".doc" or ".docx" or ".xls" or ".xlsx";
        }

        public static bool ContainsZplMarkers(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return false;

            return content.IndexOf("^XA", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   content.IndexOf("^XZ", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return "Etiqueta";

            var invalidChars = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(fileName.Length);
            foreach (var c in fileName)
            {
                sb.Append(invalidChars.Contains(c) ? '_' : c);
            }
            return sb.ToString().Trim();
        }

        private static string EnsureUniqueFilePath(string desiredPath, HashSet<string>? sessionPaths = null)
        {
            if (!File.Exists(desiredPath) && (sessionPaths == null || !sessionPaths.Contains(desiredPath)))
                return desiredPath;

            string dir = Path.GetDirectoryName(desiredPath) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(desiredPath);
            string ext = Path.GetExtension(desiredPath);

            int counter = 1;
            while (true)
            {
                string candidate = Path.Combine(dir, $"{baseName}_{counter}{ext}");
                if (!File.Exists(candidate) && (sessionPaths == null || !sessionPaths.Contains(candidate)))
                    return candidate;
                counter++;
            }
        }
    }
}

