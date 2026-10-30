using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using ZPL2PDF.Application.Services;
using ZPL2PDF.Shared.Constants;

namespace ZPL2PDF.Tests.UnitTests.Application
{
    public class BatchZipConversionServiceTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly BatchZipConversionService _service;

        public BatchZipConversionServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"ZPL2PDF_BatchTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _service = new BatchZipConversionService();
        }

        [Fact]
        public void LoadFromPaths_WithZipAndFolder_ExtractsAllValidZplItems()
        {
            // Arrange
            string folderPath = Path.Combine(_tempDir, "InputFolder");
            Directory.CreateDirectory(folderPath);

            File.WriteAllText(
                Path.Combine(folderPath, "label1.zpl"),
                "^XA^FO50,50^A0N,40,40^FDLabel 1^FS^XZ",
                Encoding.UTF8);

            string zipPath = Path.Combine(_tempDir, "labels_bundle.zip");
            using (var fs = new FileStream(zipPath, FileMode.Create))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var entry1 = archive.CreateEntry("pedido_01.txt");
                using (var writer = new StreamWriter(entry1.Open(), Encoding.UTF8))
                {
                    writer.Write("^XA^FO50,50^A0N,40,40^FDZip Label 1^FS^XZ\n^XA^FO50,50^A0N,40,40^FDZip Label 2^FS^XZ");
                }

                // File without extension inside ZIP (common in carrier exports)
                var entry2 = archive.CreateEntry("etiqueta_sem_extensao");
                using (var writer = new StreamWriter(entry2.Open(), Encoding.UTF8))
                {
                    writer.Write("^XA^FO50,50^A0N,40,40^FDCarrier Label^FS^XZ");
                }
            }

            // Act
            var items = _service.LoadFromPaths(new[] { folderPath, zipPath }, includeSubfolders: true, detectContentWithoutZplExtension: true);

            // Assert
            items.Should().HaveCount(3);
            items[0].LabelCount.Should().Be(1);
            items[1].LabelCount.Should().Be(2);
            items[2].LabelCount.Should().Be(1);
        }

        [Fact]
        public async Task ConvertBatchAsync_SeparateAndMergedModes_GeneratesPdfsAndZip()
        {
            // Arrange
            var item1 = _service.CreateFromText("^XA^FO50,50^A0N,40,40^FDItem 1^FS^XZ", "etiqueta_A")!;
            var item2 = _service.CreateFromText("^XA^FO50,50^A0N,40,40^FDItem 2^FS^XZ", "etiqueta_B")!;
            var queue = new[] { item1, item2 };

            string separateOut = Path.Combine(_tempDir, "OutSeparate");
            string mergedOut = Path.Combine(_tempDir, "OutMerged");

            // Act 1: Separate PDFs + Output ZIP
            var summarySeparate = await _service.ConvertBatchAsync(
                queue,
                separateOut,
                BatchOutputMode.SeparatePdfPerFile,
                mergedFileName: null,
                alsoCreateOutputZip: true,
                width: 10,
                height: 15,
                unit: "cm",
                dpi: 203,
                rendererEngine: RendererEngine.Offline);

            // Act 2: Merged Single PDF
            var summaryMerged = await _service.ConvertBatchAsync(
                queue,
                mergedOut,
                BatchOutputMode.MergedSinglePdf,
                mergedFileName: "Unificado.pdf",
                alsoCreateOutputZip: false,
                width: 10,
                height: 15,
                unit: "cm",
                dpi: 203,
                rendererEngine: RendererEngine.Offline);

            // Assert
            summarySeparate.SuccessCount.Should().Be(2);
            summarySeparate.GeneratedPdfPaths.Should().HaveCount(2);
            summarySeparate.GeneratedZipPath.Should().NotBeNullOrWhiteSpace();
            File.Exists(summarySeparate.GeneratedZipPath!).Should().BeTrue();

            summaryMerged.SuccessCount.Should().Be(2);
            summaryMerged.TotalPagesGenerated.Should().Be(2);
            summaryMerged.GeneratedPdfPaths.Should().ContainSingle();
            File.Exists(summaryMerged.GeneratedPdfPaths[0]).Should().BeTrue();
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}

