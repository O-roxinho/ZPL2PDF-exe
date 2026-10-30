using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ZPL2PDF.Application.Services;
using ZPL2PDF.Shared.Constants;

namespace ZPL2PDF.Presentation.Gui
{
    /// <summary>
    /// Main Windows Forms interface for easy ZPL / Folder / ZIP to PDF conversion.
    /// </summary>
    public class MainForm : Form
    {
        private readonly BatchZipConversionService _batchService;
        private readonly BindingList<ZplQueueItem> _queueItems;
        private CancellationTokenSource? _conversionCts;

        private SplitContainer _middleSplit = null!;

        // Top toolbar controls
        private Button _btnAddFolder = null!;
        private Button _btnAddZip = null!;
        private Button _btnAddFiles = null!;
        private Button _btnPasteClipboard = null!;
        private Button _btnClearQueue = null!;
        private Label _lblQueueCounter = null!;

        // Left side: Tabs (Queue & ZPL Editor)
        private TabControl _tabControlLeft = null!;
        private DataGridView _gridQueue = null!;
        private CheckBox _chkIncludeSubfolders = null!;
        private CheckBox _chkDetectAnyFileWithZpl = null!;
        private Button _btnRemoveSelected = null!;
        private Button _btnOpenSelectedPdf = null!;

        // Editor tab controls
        private TextBox _txtZplEditor = null!;
        private Button _btnAddEditorToQueue = null!;
        private Button _btnUpdateSelectedFromEditor = null!;
        private Button _btnPreviewEditorZpl = null!;
        private Button _btnLoadSampleZpl = null!;

        // Right side: Preview controls
        private PictureBox _picPreview = null!;
        private Label _lblPreviewInfo = null!;
        private Label _lblPageIndicator = null!;
        private Button _btnPrevPage = null!;
        private Button _btnNextPage = null!;
        private Button _btnRefreshPreview = null!;
        private Button _btnSaveSinglePreviewPdf = null!;
        private List<byte[]> _currentPreviewPages = new List<byte[]>();
        private int _currentPreviewPageIndex = 0;
        private string _currentPreviewZpl = string.Empty;

        // Bottom settings controls
        private TextBox _txtOutputFolder = null!;
        private Button _btnBrowseOutput = null!;
        private Button _btnOpenOutputFolder = null!;
        private RadioButton _rbSeparatePdfs = null!;
        private RadioButton _rbMergedPdf = null!;
        private TextBox _txtMergedFileName = null!;
        private CheckBox _chkAlsoCreateZip = null!;
        private CheckBox _chkOpenFolderAfterConvert = null!;

        // Dimensions & Engine controls
        private ComboBox _cmbSizePreset = null!;
        private NumericUpDown _numWidth = null!;
        private NumericUpDown _numHeight = null!;
        private ComboBox _cmbUnit = null!;
        private ComboBox _cmbDpi = null!;
        private ComboBox _cmbRenderer = null!;

        // Footer action controls
        private ProgressBar _progressBar = null!;
        private Label _lblStatus = null!;
        private Button _btnConvertNow = null!;

        public MainForm(IEnumerable<string>? initialPaths = null)
        {
            _batchService = new BatchZipConversionService();
            _queueItems = new BindingList<ZplQueueItem>();

            InitializeComponent();
            InitializeDefaults();

            Shown += (s, e) =>
            {
                try
                {
                    if (_middleSplit.Width > 400)
                    {
                        _middleSplit.SplitterDistance = (int)(_middleSplit.Width * 0.56);
                    }
                }
                catch
                {
                    // Ignore splitter distance errors on minimized windows
                }

                if (initialPaths != null)
                {
                    var validPaths = initialPaths.Where(p => !string.IsNullOrWhiteSpace(p) && (File.Exists(p) || Directory.Exists(p))).ToList();
                    if (validPaths.Count > 0)
                    {
                        AddPathsToQueue(validPaths);
                    }
                }
            };
        }

        private void InitializeComponent()
        {
            Text = "ZPL2PDF Visual — Conversor Fácil de ZPL e ZIP para PDF";
            Size = new Size(1260, 850);
            MinimumSize = new Size(1040, 720);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(241, 245, 249);
            AllowDrop = true;

            try
            {
                string iconPath = Path.Combine(AppContext.BaseDirectory, "docs", "Image", "ZPL2PDF.ico");
                if (File.Exists(iconPath))
                {
                    Icon = new Icon(iconPath);
                }
                else
                {
                    Icon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);
                }
            }
            catch
            {
                // Ignore icon errors
            }

            DragEnter += MainForm_DragEnter;
            DragDrop += MainForm_DragDrop;

            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(12)
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 114F)); // Row 0: Top Header + Action Bar
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // Row 1: Middle Split (Queue + Preview)
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 188F)); // Row 2: Bottom Settings (Output + Size)
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74F));  // Row 3: Footer (Progress + Convert Button)

            mainLayout.Controls.Add(BuildTopPanel(), 0, 0);
            mainLayout.Controls.Add(BuildMiddleSplitPanel(), 0, 1);
            mainLayout.Controls.Add(BuildSettingsPanel(), 0, 2);
            mainLayout.Controls.Add(BuildFooterPanel(), 0, 3);

            Controls.Add(mainLayout);
        }

        private Control BuildTopPanel()
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(14, 8, 14, 8),
                Margin = new Padding(0, 0, 0, 8)
            };

            var topGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Margin = new Padding(0)
            };
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250F));
            topGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            topGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var titleStack = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0)
            };
            titleStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            titleStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
            titleStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var lblTitle = new Label
            {
                Text = "📦 Conversor ZPL para PDF  —  Pastas, Arquivos .ZIP e Etiquetas",
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var lblSubtitle = new Label
            {
                Text = "Dica: Use os botões abaixo ou ARRASTE pastas, arquivos .ZIP ou .ZPL/.TXT para qualquer lugar desta janela!",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 116, 139),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            titleStack.Controls.Add(lblTitle, 0, 0);
            titleStack.Controls.Add(lblSubtitle, 0, 1);

            var buttonFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0, 4, 0, 0)
            };

            _btnAddFolder = CreateToolbarButton("📁 Escolher Pasta...", Color.FromArgb(37, 99, 235), Color.White, 155);
            _btnAddFolder.Click += BtnAddFolder_Click;

            _btnAddZip = CreateToolbarButton("🗜️ Escolher .ZIP...", Color.FromArgb(124, 58, 237), Color.White, 165);
            _btnAddZip.Click += BtnAddZip_Click;

            _btnAddFiles = CreateToolbarButton("📄 Escolher ZPL...", Color.FromArgb(15, 118, 110), Color.White, 160);
            _btnAddFiles.Click += BtnAddFiles_Click;

            _btnPasteClipboard = CreateToolbarButton("📋 Colar ZPL", Color.FromArgb(71, 85, 105), Color.White, 118);
            _btnPasteClipboard.Click += BtnPasteClipboard_Click;

            _btnClearQueue = CreateToolbarButton("🗑️ Limpar Lista", Color.FromArgb(241, 245, 249), Color.FromArgb(185, 28, 28), 125);
            _btnClearQueue.Click += BtnClearQueue_Click;

            buttonFlow.Controls.AddRange(new Control[]
            {
                _btnAddFolder,
                _btnAddZip,
                _btnAddFiles,
                _btnPasteClipboard,
                _btnClearQueue
            });

            _lblQueueCounter = new Label
            {
                Text = "0 arquivos na fila (0 etiquetas)",
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(37, 99, 235),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight
            };

            topGrid.Controls.Add(titleStack, 0, 0);
            topGrid.SetColumnSpan(titleStack, 2);
            topGrid.Controls.Add(buttonFlow, 0, 1);
            topGrid.Controls.Add(_lblQueueCounter, 1, 1);

            card.Controls.Add(topGrid);
            return card;
        }

        private Control BuildMiddleSplitPanel()
        {
            _middleSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 640,
                SplitterWidth = 8,
                Margin = new Padding(0, 0, 0, 8)
            };

            _tabControlLeft = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular)
            };

            var tabQueue = new TabPage("📋 Fila de Arquivos e ZIPs")
            {
                BackColor = Color.White,
                Padding = new Padding(8)
            };
            tabQueue.Controls.Add(BuildQueueTabContent());

            var tabEditor = new TabPage("✏️ Colar / Editar Código ZPL")
            {
                BackColor = Color.White,
                Padding = new Padding(8)
            };
            tabEditor.Controls.Add(BuildEditorTabContent());

            _tabControlLeft.TabPages.Add(tabQueue);
            _tabControlLeft.TabPages.Add(tabEditor);

            _middleSplit.Panel1.Controls.Add(_tabControlLeft);
            _middleSplit.Panel2.Controls.Add(BuildPreviewPanel());

            return _middleSplit;
        }

        private Control BuildQueueTabContent()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

            _gridQueue = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                DataSource = _queueItems,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = true,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowTemplate = { Height = 28 }
            };

            _gridQueue.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(30, 41, 59),
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                Padding = new Padding(4)
            };
            _gridQueue.EnableHeadersVisualStyles = false;

            _gridQueue.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(ZplQueueItem.DisplayName),
                HeaderText = "Arquivo / Item no ZIP",
                FillWeight = 42
            });
            _gridQueue.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(ZplQueueItem.SourceDescription),
                HeaderText = "Origem",
                FillWeight = 23
            });
            _gridQueue.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(ZplQueueItem.LabelCount),
                HeaderText = "Etiquetas",
                FillWeight = 12,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });
            _gridQueue.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(ZplQueueItem.Status),
                HeaderText = "Status",
                FillWeight = 23
            });

            _gridQueue.SelectionChanged += GridQueue_SelectionChanged;
            _gridQueue.CellDoubleClick += GridQueue_CellDoubleClick;

            var bottomBar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0, 4, 0, 0)
            };
            bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118F));
            bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108F));
            bottomBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var checkFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0, 6, 0, 0)
            };

            _chkIncludeSubfolders = new CheckBox
            {
                Text = "Incluir subpastas",
                Checked = true,
                AutoSize = true,
                Margin = new Padding(0, 0, 12, 0)
            };

            _chkDetectAnyFileWithZpl = new CheckBox
            {
                Text = "Detectar ZPL sem extensão .zpl",
                Checked = true,
                AutoSize = true
            };

            checkFlow.Controls.Add(_chkIncludeSubfolders);
            checkFlow.Controls.Add(_chkDetectAnyFileWithZpl);

            _btnRemoveSelected = new Button
            {
                Text = "Remover Item",
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 2, 4, 2),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(248, 250, 252),
                ForeColor = Color.FromArgb(71, 85, 105),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnRemoveSelected.Click += BtnRemoveSelected_Click;

            _btnOpenSelectedPdf = new Button
            {
                Text = "Abrir PDF",
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 2, 0, 2),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(239, 246, 255),
                ForeColor = Color.FromArgb(29, 78, 216),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnOpenSelectedPdf.Click += BtnOpenSelectedPdf_Click;

            bottomBar.Controls.Add(checkFlow, 0, 0);
            bottomBar.Controls.Add(_btnRemoveSelected, 1, 0);
            bottomBar.Controls.Add(_btnOpenSelectedPdf, 2, 0);

            layout.Controls.Add(_gridQueue, 0, 0);
            layout.Controls.Add(bottomBar, 0, 1);

            return layout;
        }

        private Control BuildEditorTabContent()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

            _txtZplEditor = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 10F, FontStyle.Regular),
                PlaceholderText = "Cole aqui seu código ZPL (^XA ... ^XZ) ou selecione um arquivo na aba anterior para inspecionar/editar seu conteúdo..."
            };

            var flowActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0, 4, 0, 0)
            };

            _btnAddEditorToQueue = CreateToolbarButton("➕ Adicionar à Fila", Color.FromArgb(37, 99, 235), Color.White, 155);
            _btnAddEditorToQueue.Height = 32;
            _btnAddEditorToQueue.Click += BtnAddEditorToQueue_Click;

            _btnUpdateSelectedFromEditor = CreateToolbarButton("💾 Salvar Edição", Color.FromArgb(15, 118, 110), Color.White, 140);
            _btnUpdateSelectedFromEditor.Height = 32;
            _btnUpdateSelectedFromEditor.Click += BtnUpdateSelectedFromEditor_Click;

            _btnPreviewEditorZpl = CreateToolbarButton("👁️ Visualizar", Color.FromArgb(71, 85, 105), Color.White, 125);
            _btnPreviewEditorZpl.Height = 32;
            _btnPreviewEditorZpl.Click += (s, e) => RenderPreviewForZpl(_txtZplEditor.Text, "Editor ZPL");

            _btnLoadSampleZpl = CreateToolbarButton("📄 Exemplo", Color.FromArgb(241, 245, 249), Color.FromArgb(51, 65, 85), 100);
            _btnLoadSampleZpl.Height = 32;
            _btnLoadSampleZpl.Click += BtnLoadSampleZpl_Click;

            flowActions.Controls.AddRange(new Control[]
            {
                _btnAddEditorToQueue,
                _btnUpdateSelectedFromEditor,
                _btnPreviewEditorZpl,
                _btnLoadSampleZpl
            });

            layout.Controls.Add(_txtZplEditor, 0, 0);
            layout.Controls.Add(flowActions, 0, 1);

            return layout;
        }

        private Control BuildPreviewPanel()
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(10)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

            var topBar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 215F));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            topBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var lblPreviewHeader = new Label
            {
                Text = "👁️ Pré-visualização da Etiqueta",
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _lblPreviewInfo = new Label
            {
                Text = "Selecione um arquivo na lista",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 116, 139),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true
            };

            topBar.Controls.Add(lblPreviewHeader, 0, 0);
            topBar.Controls.Add(_lblPreviewInfo, 1, 0);

            var previewContainer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(51, 65, 85),
                Padding = new Padding(12)
            };

            _picPreview = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(51, 65, 85)
            };
            previewContainer.Controls.Add(_picPreview);

            var navBar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 1,
                Margin = new Padding(0, 4, 0, 0)
            };
            navBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84F));
            navBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            navBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84F));
            navBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            navBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            navBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _btnPrevPage = new Button
            {
                Text = "◀ Anterior",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 4, 2),
                FlatStyle = FlatStyle.Flat,
                Enabled = false,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnPrevPage.Click += (s, e) => ChangePreviewPage(-1);

            _lblPageIndicator = new Label
            {
                Text = "Página 0 / 0",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold)
            };

            _btnNextPage = new Button
            {
                Text = "Próxima ▶",
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 2, 4, 2),
                FlatStyle = FlatStyle.Flat,
                Enabled = false,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnNextPage.Click += (s, e) => ChangePreviewPage(1);

            _btnRefreshPreview = new Button
            {
                Text = "🔄 Atualizar",
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 2, 4, 2),
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnRefreshPreview.Click += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(_currentPreviewZpl))
                {
                    RenderPreviewForZpl(_currentPreviewZpl, "Etiqueta Atual");
                }
            };

            _btnSaveSinglePreviewPdf = new Button
            {
                Text = "💾 Salvar PDF...",
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 2, 0, 2),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(239, 246, 255),
                ForeColor = Color.FromArgb(29, 78, 216),
                Enabled = false,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnSaveSinglePreviewPdf.Click += BtnSaveSinglePreviewPdf_Click;

            navBar.Controls.Add(_btnPrevPage, 0, 0);
            navBar.Controls.Add(_lblPageIndicator, 1, 0);
            navBar.Controls.Add(_btnNextPage, 2, 0);
            navBar.Controls.Add(_btnRefreshPreview, 3, 0);
            navBar.Controls.Add(_btnSaveSinglePreviewPdf, 4, 0);

            layout.Controls.Add(topBar, 0, 0);
            layout.Controls.Add(previewContainer, 0, 1);
            layout.Controls.Add(navBar, 0, 2);

            card.Controls.Add(layout);
            return card;
        }

        private Control BuildSettingsPanel()
        {
            var splitSettings = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 8)
            };
            splitSettings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53F));
            splitSettings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47F));
            splitSettings.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // Left Group: Output Destination & Mode
            var grpOutput = new GroupBox
            {
                Text = " 📂 1. Onde e como salvar os PDFs ",
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                Padding = new Padding(10, 6, 10, 6),
                Margin = new Padding(0, 0, 6, 0)
            };

            var outputLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Margin = new Padding(0)
            };
            outputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            outputLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            outputLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            outputLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            outputLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // Row 0: Folder selection bar
            var folderBar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = new Padding(0)
            };
            folderBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
            folderBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            folderBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94F));
            folderBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94F));
            folderBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var lblFolder = new Label
            {
                Text = "Pasta de destino:",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _txtOutputFolder = new TextBox
            {
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 4, 6, 4)
            };

            _btnBrowseOutput = new Button
            {
                Text = "Escolher...",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 3, 6, 3),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(241, 245, 249),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnBrowseOutput.Click += BtnBrowseOutput_Click;

            _btnOpenOutputFolder = new Button
            {
                Text = "Abrir Pasta",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 3, 0, 3),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(241, 245, 249),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnOpenOutputFolder.Click += BtnOpenOutputFolder_Click;

            folderBar.Controls.Add(lblFolder, 0, 0);
            folderBar.Controls.Add(_txtOutputFolder, 1, 0);
            folderBar.Controls.Add(_btnBrowseOutput, 2, 0);
            folderBar.Controls.Add(_btnOpenOutputFolder, 3, 0);

            // Row 1: Separate PDFs radio
            _rbSeparatePdfs = new RadioButton
            {
                Text = "Gerar 1 PDF separado para cada arquivo ZPL",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Checked = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 0, 0, 0)
            };
            _rbSeparatePdfs.CheckedChanged += OutputMode_CheckedChanged;

            // Row 2: Merged PDF radio + filename
            var mergedRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            mergedRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 225F));
            mergedRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mergedRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _rbMergedPdf = new RadioButton
            {
                Text = "Juntar TODOS em um único PDF:",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 0, 4, 0)
            };
            _rbMergedPdf.CheckedChanged += OutputMode_CheckedChanged;

            _txtMergedFileName = new TextBox
            {
                Text = "Etiquetas_Consolidadas.pdf",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Enabled = false,
                Margin = new Padding(0, 3, 0, 3)
            };

            mergedRow.Controls.Add(_rbMergedPdf, 0, 0);
            mergedRow.Controls.Add(_txtMergedFileName, 1, 0);

            // Row 3: Extra checkboxes
            var optionsRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            optionsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 295F));
            optionsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            optionsRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _chkAlsoCreateZip = new CheckBox
            {
                Text = "Gerar também um arquivo .ZIP com os PDFs",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 0, 8, 0)
            };

            _chkOpenFolderAfterConvert = new CheckBox
            {
                Text = "Abrir pasta ao concluir",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Checked = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 0, 0, 0)
            };

            optionsRow.Controls.Add(_chkAlsoCreateZip, 0, 0);
            optionsRow.Controls.Add(_chkOpenFolderAfterConvert, 1, 0);

            outputLayout.Controls.Add(folderBar, 0, 0);
            outputLayout.Controls.Add(_rbSeparatePdfs, 0, 1);
            outputLayout.Controls.Add(mergedRow, 0, 2);
            outputLayout.Controls.Add(optionsRow, 0, 3);
            grpOutput.Controls.Add(outputLayout);

            // Right Group: Label Size, DPI & Renderer
            var grpDimensions = new GroupBox
            {
                Text = " 📐 2. Tamanho da Etiqueta, Qualidade (DPI) e Motor ",
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                Padding = new Padding(10, 6, 10, 6),
                Margin = new Padding(6, 0, 0, 0)
            };

            var dimLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0)
            };
            dimLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            dimLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            dimLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            dimLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // Row 0: Preset
            var presetRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            presetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78F));
            presetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            presetRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var lblPreset = new Label
            {
                Text = "Formato:",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _cmbSizePreset = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 6, 0, 4)
            };
            _cmbSizePreset.Items.AddRange(new object[]
            {
                "10 x 15 cm (Padrão E-commerce / Envios / Correios / ML / Shopee)",
                "Automático (Extrair tamanho ^PW/^LL do próprio ZPL)",
                "10 x 10 cm (4 x 4 pol - Logística)",
                "10 x 5 cm (Produto / Caixa)",
                "8 x 4 cm (Etiqueta Média)",
                "6 x 4 cm (Etiqueta Pequena)",
                "Personalizado (Definir abaixo)"
            });
            _cmbSizePreset.SelectedIndexChanged += CmbSizePreset_SelectedIndexChanged;

            presetRow.Controls.Add(lblPreset, 0, 0);
            presetRow.Controls.Add(_cmbSizePreset, 1, 0);

            // Row 1: Width, Height, Unit
            var sizeNumbersRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 6,
                RowCount = 1,
                Margin = new Padding(0)
            };
            sizeNumbersRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78F));
            sizeNumbersRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84F));
            sizeNumbersRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58F));
            sizeNumbersRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84F));
            sizeNumbersRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52F));
            sizeNumbersRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            sizeNumbersRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var lblWidth = new Label
            {
                Text = "Largura:",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _numWidth = new NumericUpDown
            {
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                DecimalPlaces = 1,
                Minimum = 0,
                Maximum = 2000,
                Value = 10.0M,
                Increment = 0.5M,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 6, 10, 4)
            };

            var lblHeight = new Label
            {
                Text = "Altura:",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _numHeight = new NumericUpDown
            {
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                DecimalPlaces = 1,
                Minimum = 0,
                Maximum = 2000,
                Value = 15.0M,
                Increment = 0.5M,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 6, 10, 4)
            };

            var lblUnit = new Label
            {
                Text = "Unid.:",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _cmbUnit = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Width = 76,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 0, 4)
            };
            _cmbUnit.Items.AddRange(new object[] { "cm", "mm", "in" });

            sizeNumbersRow.Controls.Add(lblWidth, 0, 0);
            sizeNumbersRow.Controls.Add(_numWidth, 1, 0);
            sizeNumbersRow.Controls.Add(lblHeight, 2, 0);
            sizeNumbersRow.Controls.Add(_numHeight, 3, 0);
            sizeNumbersRow.Controls.Add(lblUnit, 4, 0);
            sizeNumbersRow.Controls.Add(_cmbUnit, 5, 0);

            // Row 2: DPI & Renderer
            var dpiEngineRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = new Padding(0)
            };
            dpiEngineRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78F));
            dpiEngineRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
            dpiEngineRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54F));
            dpiEngineRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            dpiEngineRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var lblDpi = new Label
            {
                Text = "Densidade:",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _cmbDpi = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 6, 10, 4)
            };
            _cmbDpi.Items.AddRange(new object[]
            {
                "203 DPI (8 dpmm)",
                "300 DPI (12 dpmm)",
                "600 DPI (24 dpmm)"
            });

            var lblRenderer = new Label
            {
                Text = "Motor:",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _cmbRenderer = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 6, 0, 4)
            };
            _cmbRenderer.Items.AddRange(new object[]
            {
                "Offline (Rápido / Sem Internet)",
                "Auto (Labelary Online + Offline)",
                "Labelary Online (Alta Fidelidade)"
            });

            dpiEngineRow.Controls.Add(lblDpi, 0, 0);
            dpiEngineRow.Controls.Add(_cmbDpi, 1, 0);
            dpiEngineRow.Controls.Add(lblRenderer, 2, 0);
            dpiEngineRow.Controls.Add(_cmbRenderer, 3, 0);

            dimLayout.Controls.Add(presetRow, 0, 0);
            dimLayout.Controls.Add(sizeNumbersRow, 0, 1);
            dimLayout.Controls.Add(dpiEngineRow, 0, 2);
            grpDimensions.Controls.Add(dimLayout);

            splitSettings.Controls.Add(grpOutput, 0, 0);
            splitSettings.Controls.Add(grpDimensions, 1, 0);

            return splitSettings;
        }

        private Control BuildFooterPanel()
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(14, 8, 14, 8),
                Margin = new Padding(0)
            };

            var footerGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            footerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 310F));
            footerGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var progressStack = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0, 0, 16, 0)
            };
            progressStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            progressStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            progressStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _lblStatus = new Label
            {
                Text = "Pronto! Escolha ou arraste uma pasta, arquivo .ZIP ou arquivos .ZPL para começar.",
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(51, 65, 85),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 0, 4),
                Style = ProgressBarStyle.Continuous
            };

            progressStack.Controls.Add(_lblStatus, 0, 0);
            progressStack.Controls.Add(_progressBar, 0, 1);

            _btnConvertNow = new Button
            {
                Text = "🚀 CONVERTER PARA PDF AGORA",
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                BackColor = Color.FromArgb(22, 163, 74),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 0, 2),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnConvertNow.FlatAppearance.BorderSize = 0;
            _btnConvertNow.Click += BtnConvertNow_Click;

            footerGrid.Controls.Add(progressStack, 0, 0);
            footerGrid.Controls.Add(_btnConvertNow, 1, 0);

            card.Controls.Add(footerGrid);
            return card;
        }

        private void InitializeDefaults()
        {
            string defaultOut = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ZPL2PDF_PDFs");
            _txtOutputFolder.Text = defaultOut;

            _cmbUnit.SelectedIndex = 0; // cm
            _cmbSizePreset.SelectedIndex = 0; // 10 x 15 cm
            _cmbDpi.SelectedIndex = 0; // 203 DPI
            _cmbRenderer.SelectedIndex = 0; // Offline
        }

        private static Button CreateToolbarButton(string text, Color backColor, Color foreColor, int width)
        {
            var btn = new Button
            {
                Text = text,
                Size = new Size(width, 34),
                BackColor = backColor,
                ForeColor = foreColor,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 8, 0),
                TextAlign = ContentAlignment.MiddleCenter
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private void CmbSizePreset_SelectedIndexChanged(object? sender, EventArgs e)
        {
            switch (_cmbSizePreset.SelectedIndex)
            {
                case 0: // 10 x 15 cm
                    _cmbUnit.SelectedItem = "cm";
                    _numWidth.Value = 10.0M;
                    _numHeight.Value = 15.0M;
                    _numWidth.Enabled = true;
                    _numHeight.Enabled = true;
                    break;
                case 1: // Automatic from ZPL
                    _numWidth.Value = 0M;
                    _numHeight.Value = 0M;
                    _numWidth.Enabled = false;
                    _numHeight.Enabled = false;
                    break;
                case 2: // 10 x 10 cm
                    _cmbUnit.SelectedItem = "cm";
                    _numWidth.Value = 10.0M;
                    _numHeight.Value = 10.0M;
                    _numWidth.Enabled = true;
                    _numHeight.Enabled = true;
                    break;
                case 3: // 10 x 5 cm
                    _cmbUnit.SelectedItem = "cm";
                    _numWidth.Value = 10.0M;
                    _numHeight.Value = 5.0M;
                    _numWidth.Enabled = true;
                    _numHeight.Enabled = true;
                    break;
                case 4: // 8 x 4 cm
                    _cmbUnit.SelectedItem = "cm";
                    _numWidth.Value = 8.0M;
                    _numHeight.Value = 4.0M;
                    _numWidth.Enabled = true;
                    _numHeight.Enabled = true;
                    break;
                case 5: // 6 x 4 cm
                    _cmbUnit.SelectedItem = "cm";
                    _numWidth.Value = 6.0M;
                    _numHeight.Value = 4.0M;
                    _numWidth.Enabled = true;
                    _numHeight.Enabled = true;
                    break;
                default: // Custom
                    if (_numWidth.Value == 0) _numWidth.Value = 10.0M;
                    if (_numHeight.Value == 0) _numHeight.Value = 15.0M;
                    _numWidth.Enabled = true;
                    _numHeight.Enabled = true;
                    break;
            }
        }

        private void OutputMode_CheckedChanged(object? sender, EventArgs e)
        {
            _txtMergedFileName.Enabled = _rbMergedPdf.Checked;
        }

        private void MainForm_DragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data != null && (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text)))
            {
                e.Effect = DragDropEffects.Copy;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void MainForm_DragDrop(object? sender, DragEventArgs e)
        {
            if (e.Data == null)
                return;

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] droppedPaths && droppedPaths.Length > 0)
                {
                    AddPathsToQueue(droppedPaths);
                }
            }
            else if (e.Data.GetDataPresent(DataFormats.Text))
            {
                string? droppedText = e.Data.GetData(DataFormats.Text) as string;
                if (!string.IsNullOrWhiteSpace(droppedText))
                {
                    AddTextItemToQueue(droppedText);
                }
            }
        }

        private void BtnAddFolder_Click(object? sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Selecione a pasta que contém os arquivos ZPL ou arquivos .ZIP",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                AddPathsToQueue(new[] { dialog.SelectedPath });
            }
        }

        private void BtnAddZip_Click(object? sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Selecione um ou mais arquivos .ZIP contendo etiquetas ZPL",
                Filter = "Arquivos Compactados ZIP (*.zip)|*.zip|Todos os arquivos (*.*)|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.FileNames.Length > 0)
            {
                AddPathsToQueue(dialog.FileNames);
            }
        }

        private void BtnAddFiles_Click(object? sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Selecione os arquivos ZPL (ou .ZIP)",
                Filter = "Etiquetas ZPL e ZIP (*.zpl;*.txt;*.prn;*.imp;*.zip)|*.zpl;*.txt;*.prn;*.imp;*.zip|Todos os arquivos (*.*)|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.FileNames.Length > 0)
            {
                AddPathsToQueue(dialog.FileNames);
            }
        }

        private void BtnPasteClipboard_Click(object? sender, EventArgs e)
        {
            if (Clipboard.ContainsFileDropList())
            {
                var fileList = Clipboard.GetFileDropList().Cast<string>().ToArray();
                AddPathsToQueue(fileList);
                return;
            }

            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                if (!AddTextItemToQueue(text))
                {
                    _txtZplEditor.Text = text;
                    _tabControlLeft.SelectedIndex = 1;
                    MessageBox.Show(
                        this,
                        "O texto da área de transferência foi colado no editor, mas não encontramos os comandos ^XA e ^XZ completos.",
                        "Texto Colado no Editor",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            else
            {
                MessageBox.Show(
                    this,
                    "A área de transferência não contém código ZPL nem arquivos copiados.",
                    "Área de Transferência Vazia",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private void BtnClearQueue_Click(object? sender, EventArgs e)
        {
            _queueItems.Clear();
            ClearPreview();
            UpdateQueueCounters();
            _lblStatus.Text = "Lista limpa.";
        }

        private void BtnRemoveSelected_Click(object? sender, EventArgs e)
        {
            if (_gridQueue.SelectedRows.Count == 0)
                return;

            var itemsToRemove = _gridQueue.SelectedRows
                .Cast<DataGridViewRow>()
                .Select(r => r.DataBoundItem as ZplQueueItem)
                .Where(i => i != null)
                .ToList();

            foreach (var item in itemsToRemove)
            {
                _queueItems.Remove(item!);
            }

            UpdateQueueCounters();
            if (_queueItems.Count == 0)
            {
                ClearPreview();
            }
        }

        private void BtnOpenSelectedPdf_Click(object? sender, EventArgs e)
        {
            if (_gridQueue.CurrentRow?.DataBoundItem is ZplQueueItem item &&
                !string.IsNullOrWhiteSpace(item.OutputPdfPath) &&
                File.Exists(item.OutputPdfPath))
            {
                OpenFileOrFolder(item.OutputPdfPath);
            }
            else
            {
                MessageBox.Show(
                    this,
                    "Converta os arquivos primeiro para abrir o PDF gerado.",
                    "PDF ainda não gerado",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private void GridQueue_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && _gridQueue.Rows[e.RowIndex].DataBoundItem is ZplQueueItem item)
            {
                if (!string.IsNullOrWhiteSpace(item.OutputPdfPath) && File.Exists(item.OutputPdfPath))
                {
                    OpenFileOrFolder(item.OutputPdfPath);
                }
            }
        }

        private void GridQueue_SelectionChanged(object? sender, EventArgs e)
        {
            if (_gridQueue.CurrentRow?.DataBoundItem is ZplQueueItem item)
            {
                _txtZplEditor.Text = item.ZplContent;
                RenderPreviewForZpl(item.ZplContent, item.DisplayName);
            }
        }

        private void BtnAddEditorToQueue_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_txtZplEditor.Text))
            {
                MessageBox.Show(this, "Digite ou cole um comando ZPL (^XA...^XZ) primeiro.", "Editor Vazio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (AddTextItemToQueue(_txtZplEditor.Text))
            {
                _tabControlLeft.SelectedIndex = 0;
            }
            else
            {
                MessageBox.Show(this, "O código informado não parece conter uma etiqueta ZPL válida (^XA...^XZ).", "ZPL Inválido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void BtnUpdateSelectedFromEditor_Click(object? sender, EventArgs e)
        {
            if (_gridQueue.CurrentRow?.DataBoundItem is ZplQueueItem selectedItem)
            {
                int count = BatchZipConversionService.CountLabels(_txtZplEditor.Text);
                if (count <= 0)
                {
                    MessageBox.Show(this, "O código ZPL não contém etiquetas válidas (^XA...^XZ).", "ZPL Inválido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                selectedItem.ZplContent = _txtZplEditor.Text;
                selectedItem.LabelCount = count;
                selectedItem.Status = "Editado (Pronto)";
                _gridQueue.Refresh();
                UpdateQueueCounters();
                RenderPreviewForZpl(selectedItem.ZplContent, selectedItem.DisplayName);
                _lblStatus.Text = $"Item '{selectedItem.DisplayName}' atualizado com sucesso.";
            }
            else
            {
                BtnAddEditorToQueue_Click(sender, e);
            }
        }

        private void BtnLoadSampleZpl_Click(object? sender, EventArgs e)
        {
            string sampleZpl =
@"^XA
^PW800
^LL1200
^FO40,40^GB720,1120,4^FS
^FO70,80^A0N,45,45^FDZPL2PDF VISUAL - ETIQUETA TESTE^FS
^FO70,145^A0N,28,28^FDEnvio Rapido / E-Commerce Brasil^FS
^FO40,195^GB720,4,4^FS
^FO70,230^A0N,32,32^FDDestinatario: Cliente Exemplo da Silva^FS
^FO70,275^A0N,28,28^FDRua das Flores, 123 - Centro^FS
^FO70,315^A0N,28,28^FDCEP: 01001-000 - Sao Paulo / SP^FS
^FO40,375^GB720,4,4^FS
^FO130,430^BY3,3,140^BCN,140,Y,N,N^FDBR1234567890123^FS
^FO70,650^A0N,26,26^FDPedido: #987654321   |   Nota Fiscal: 000.123.456^FS
^FO70,700^A0N,26,26^FDPeso: 1,250 kg       |   Volume: 1/1^FS
^XZ";
            _txtZplEditor.Text = sampleZpl;
            RenderPreviewForZpl(sampleZpl, "Etiqueta de Exemplo");
        }

        private void AddPathsToQueue(IEnumerable<string> paths)
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                var pathList = paths.ToList();
                var loadedItems = _batchService.LoadFromPaths(
                    pathList,
                    includeSubfolders: _chkIncludeSubfolders.Checked,
                    detectContentWithoutZplExtension: _chkDetectAnyFileWithZpl.Checked);

                if (loadedItems.Count == 0)
                {
                    MessageBox.Show(
                        this,
                        "Nenhum arquivo ZPL válido (contendo ^XA ... ^XZ) foi encontrado nos arquivos/pastas/ZIP selecionados.",
                        "Nenhuma Etiqueta Encontrada",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                string firstPath = pathList[0];
                string? parentDir = Directory.Exists(firstPath)
                    ? firstPath
                    : Path.GetDirectoryName(firstPath);

                if (!string.IsNullOrWhiteSpace(parentDir) && _queueItems.Count == 0)
                {
                    _txtOutputFolder.Text = Path.Combine(parentDir, "PDFs_Convertidos");
                }

                foreach (var item in loadedItems)
                {
                    _queueItems.Add(item);
                }

                UpdateQueueCounters();
                _tabControlLeft.SelectedIndex = 0;

                if (_gridQueue.Rows.Count > 0)
                {
                    int selectIndex = _gridQueue.Rows.Count - loadedItems.Count;
                    if (selectIndex >= 0 && selectIndex < _gridQueue.Rows.Count)
                    {
                        _gridQueue.ClearSelection();
                        _gridQueue.Rows[selectIndex].Selected = true;
                        _gridQueue.CurrentCell = _gridQueue.Rows[selectIndex].Cells[0];
                    }
                }

                int totalLabelsAdded = loadedItems.Sum(i => i.LabelCount);
                _lblStatus.Text = $"Adicionados {loadedItems.Count} arquivo(s) ({totalLabelsAdded} etiqueta(s)) à fila!";
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private bool AddTextItemToQueue(string zplText)
        {
            var item = _batchService.CreateFromText(zplText);
            if (item == null)
                return false;

            _queueItems.Add(item);
            UpdateQueueCounters();

            int lastIdx = _gridQueue.Rows.Count - 1;
            if (lastIdx >= 0)
            {
                _gridQueue.ClearSelection();
                _gridQueue.Rows[lastIdx].Selected = true;
                _gridQueue.CurrentCell = _gridQueue.Rows[lastIdx].Cells[0];
            }

            _lblStatus.Text = $"Código ZPL colado adicionado à fila ({item.LabelCount} etiqueta(s)).";
            return true;
        }

        private void UpdateQueueCounters()
        {
            int totalFiles = _queueItems.Count;
            int totalLabels = _queueItems.Sum(i => i.LabelCount);
            _lblQueueCounter.Text = $"{totalFiles} arquivo(s) na fila ({totalLabels} etiquetas)";
        }

        private (double Width, double Height, string Unit, int Dpi, RendererEngine Engine) GetConversionParameters()
        {
            double width = _cmbSizePreset.SelectedIndex == 1 ? 0 : (double)_numWidth.Value;
            double height = _cmbSizePreset.SelectedIndex == 1 ? 0 : (double)_numHeight.Value;
            string unit = _cmbUnit.SelectedItem?.ToString() ?? "cm";

            int dpi = _cmbDpi.SelectedIndex switch
            {
                1 => 300,
                2 => 600,
                _ => 203
            };

            RendererEngine engine = _cmbRenderer.SelectedIndex switch
            {
                1 => RendererEngine.Auto,
                2 => RendererEngine.Labelary,
                _ => RendererEngine.Offline
            };

            return (width, height, unit, dpi, engine);
        }

        private async void RenderPreviewForZpl(string zplContent, string title)
        {
            if (string.IsNullOrWhiteSpace(zplContent))
            {
                ClearPreview();
                return;
            }

            _currentPreviewZpl = zplContent;
            _lblPreviewInfo.Text = $"Renderizando: {title}...";

            var (width, height, unit, dpi, engine) = GetConversionParameters();

            try
            {
                var pages = await Task.Run(() =>
                    _batchService.RenderPreviewImages(zplContent, width, height, unit, dpi, engine));

                _currentPreviewPages = pages;
                _currentPreviewPageIndex = 0;

                if (_currentPreviewPages.Count > 0)
                {
                    ShowCurrentPreviewPage();
                    _lblPreviewInfo.Text = $"{title} ({_currentPreviewPages.Count} pág.)";
                    _btnSaveSinglePreviewPdf.Enabled = true;
                }
                else
                {
                    ClearPreview();
                    _lblPreviewInfo.Text = "Nenhuma imagem gerada para este ZPL.";
                }
            }
            catch (Exception ex)
            {
                ClearPreview();
                _lblPreviewInfo.Text = $"Erro no preview: {ex.Message}";
            }
        }

        private void ShowCurrentPreviewPage()
        {
            if (_currentPreviewPages.Count == 0 ||
                _currentPreviewPageIndex < 0 ||
                _currentPreviewPageIndex >= _currentPreviewPages.Count)
            {
                ClearPreview();
                return;
            }

            var oldImage = _picPreview.Image;
            using var ms = new MemoryStream(_currentPreviewPages[_currentPreviewPageIndex]);
            _picPreview.Image = Image.FromStream(ms);
            oldImage?.Dispose();

            _lblPageIndicator.Text = $"Página {_currentPreviewPageIndex + 1} / {_currentPreviewPages.Count}";
            _btnPrevPage.Enabled = _currentPreviewPageIndex > 0;
            _btnNextPage.Enabled = _currentPreviewPageIndex < _currentPreviewPages.Count - 1;
        }

        private void ChangePreviewPage(int delta)
        {
            int nextIndex = _currentPreviewPageIndex + delta;
            if (nextIndex >= 0 && nextIndex < _currentPreviewPages.Count)
            {
                _currentPreviewPageIndex = nextIndex;
                ShowCurrentPreviewPage();
            }
        }

        private void ClearPreview()
        {
            var oldImage = _picPreview.Image;
            _picPreview.Image = null;
            oldImage?.Dispose();

            _currentPreviewPages.Clear();
            _currentPreviewPageIndex = 0;
            _lblPageIndicator.Text = "Página 0 / 0";
            _btnPrevPage.Enabled = false;
            _btnNextPage.Enabled = false;
            _btnSaveSinglePreviewPdf.Enabled = false;
        }

        private void BtnSaveSinglePreviewPdf_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentPreviewZpl))
                return;

            using var sfd = new SaveFileDialog
            {
                Title = "Salvar PDF desta etiqueta",
                Filter = "Arquivo PDF (*.pdf)|*.pdf",
                FileName = $"Etiqueta_{DateTime.Now:yyyyMMdd_HHmmss}.pdf",
                InitialDirectory = Directory.Exists(_txtOutputFolder.Text) ? _txtOutputFolder.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (sfd.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(sfd.FileName))
            {
                try
                {
                    var (width, height, unit, dpi, engine) = GetConversionParameters();
                    int pages = _batchService.ConvertSingleZplToPdfFile(_currentPreviewZpl, sfd.FileName, width, height, unit, dpi, engine);
                    _lblStatus.Text = $"PDF salvo com sucesso ({pages} página(s)): {sfd.FileName}";
                    OpenFileOrFolder(sfd.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Erro ao salvar PDF: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BtnBrowseOutput_Click(object? sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Escolha a pasta onde os arquivos PDF serão salvos",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
                SelectedPath = Directory.Exists(_txtOutputFolder.Text) ? _txtOutputFolder.Text : string.Empty
            };

            if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                _txtOutputFolder.Text = dialog.SelectedPath;
            }
        }

        private void BtnOpenOutputFolder_Click(object? sender, EventArgs e)
        {
            string folder = _txtOutputFolder.Text.Trim();
            if (string.IsNullOrWhiteSpace(folder))
                return;

            if (!Directory.Exists(folder))
            {
                try
                {
                    Directory.CreateDirectory(folder);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Não foi possível criar/abrir a pasta: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            OpenFileOrFolder(folder);
        }

        private async void BtnConvertNow_Click(object? sender, EventArgs e)
        {
            if (_conversionCts != null)
            {
                _conversionCts.Cancel();
                return;
            }

            if (_queueItems.Count == 0 && !string.IsNullOrWhiteSpace(_txtZplEditor.Text))
            {
                AddTextItemToQueue(_txtZplEditor.Text);
            }

            if (_queueItems.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "Adicione pelo menos uma pasta, arquivo .ZIP ou arquivo .ZPL na lista antes de converter!",
                    "Fila Vazia",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string outputFolder = _txtOutputFolder.Text.Trim();
            if (string.IsNullOrWhiteSpace(outputFolder))
            {
                MessageBox.Show(this, "Escolha uma pasta de destino para salvar os PDFs.", "Pasta de Destino", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var (width, height, unit, dpi, engine) = GetConversionParameters();
            var outputMode = _rbMergedPdf.Checked ? BatchOutputMode.MergedSinglePdf : BatchOutputMode.SeparatePdfPerFile;
            string mergedName = _txtMergedFileName.Text.Trim();
            bool createZip = _chkAlsoCreateZip.Checked;

            _conversionCts = new CancellationTokenSource();
            SetUiBusyState(true);

            _progressBar.Minimum = 0;
            _progressBar.Maximum = _queueItems.Count;
            _progressBar.Value = 0;

            var progress = new Progress<BatchConversionProgress>(report =>
            {
                if (report.CurrentIndex <= _progressBar.Maximum)
                {
                    _progressBar.Value = report.CurrentIndex;
                }
                _lblStatus.Text = report.StatusMessage;
                _gridQueue.Refresh();
            });

            try
            {
                var itemsSnapshot = _queueItems.ToList();
                var summary = await _batchService.ConvertBatchAsync(
                    itemsSnapshot,
                    outputFolder,
                    outputMode,
                    mergedName,
                    createZip,
                    width,
                    height,
                    unit,
                    dpi,
                    engine,
                    progress,
                    _conversionCts.Token);

                _gridQueue.Refresh();
                _progressBar.Value = _progressBar.Maximum;

                string resultMessage =
                    $"✅ Conversão concluída!\n\n" +
                    $"• Arquivos processados com sucesso: {summary.SuccessCount} de {summary.TotalItems}\n" +
                    $"• Total de páginas/etiquetas geradas: {summary.TotalPagesGenerated}\n" +
                    $"• Pasta de saída: {outputFolder}";

                if (!string.IsNullOrWhiteSpace(summary.GeneratedZipPath))
                {
                    resultMessage += $"\n• Arquivo ZIP criado: {Path.GetFileName(summary.GeneratedZipPath)}";
                }

                if (summary.ErrorCount > 0)
                {
                    resultMessage += $"\n\n⚠️ Erros ({summary.ErrorCount}):\n" + string.Join("\n", summary.Errors.Take(5));
                }

                _lblStatus.Text = $"Concluído! {summary.SuccessCount} arquivo(s) convertido(s), {summary.TotalPagesGenerated} página(s) gerada(s).";

                if (_chkOpenFolderAfterConvert.Checked && summary.SuccessCount > 0)
                {
                    OpenFileOrFolder(outputFolder);
                }

                MessageBox.Show(
                    this,
                    resultMessage,
                    "Conversão Finalizada",
                    MessageBoxButtons.OK,
                    summary.ErrorCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                _lblStatus.Text = "Conversão cancelada pelo usuário.";
                _gridQueue.Refresh();
            }
            catch (Exception ex)
            {
                _lblStatus.Text = $"Erro durante a conversão: {ex.Message}";
                MessageBox.Show(this, $"Ocorreu um erro durante a conversão:\n{ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _conversionCts?.Dispose();
                _conversionCts = null;
                SetUiBusyState(false);
            }
        }

        private void SetUiBusyState(bool isBusy)
        {
            _btnAddFolder.Enabled = !isBusy;
            _btnAddZip.Enabled = !isBusy;
            _btnAddFiles.Enabled = !isBusy;
            _btnPasteClipboard.Enabled = !isBusy;
            _btnClearQueue.Enabled = !isBusy;
            _btnBrowseOutput.Enabled = !isBusy;

            if (isBusy)
            {
                _btnConvertNow.Text = "⏹️ CANCELAR CONVERSÃO";
                _btnConvertNow.BackColor = Color.FromArgb(220, 38, 38);
            }
            else
            {
                _btnConvertNow.Text = "🚀 CONVERTER PARA PDF AGORA";
                _btnConvertNow.BackColor = Color.FromArgb(22, 163, 74);
            }
        }

        private static void OpenFileOrFolder(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignore shell launch errors
            }
        }
    }
}
