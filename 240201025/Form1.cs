using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace Proje3
{
    public partial class Form1 : Form
    {
        private DatabaseManager _databaseManager = null!;
        private List<TableSchema> _tableSchemas = null!;
        private List<FlatRecord> _extractedRecords = null!;
        private string _databaseFilePath = null!;
        private int _persistedRecordLogCount = 0;

        private Panel _panelTopControls = null!;
        private Label _labelApplicationTitle = null!;
        private Button _buttonSelectJson = null!;
        private Button _buttonConvert = null!;
        private Button _buttonResetDatabase = null!;
        private Label _labelFileStatus = null!;

        private Panel _panelLeftContainer = null!;
        private Label _labelJsonTreeHeader = null!;
        private TreeView _treeViewJsonStructure = null!;

        private Panel _panelLogContainer = null!;
        private Label _labelSqlLogHeader = null!;
        private RichTextBox _richTextBoxSqlLog = null!;

        private Panel _panelRightContainer = null!;
        private Label _labelQueryHeader = null!;
        private TextBox _textBoxSqlEditor = null!;
        private Button _buttonExecuteQuery = null!;
        private TabControl _tabControlTables = null!;

        private SplitContainer _splitContainerMain = null!;
        private SplitContainer _splitContainerLeft = null!;

        public Form1()
        {
            InitializeComponent();
            BuildUserInterface();
            InitializeNewDatabase();

            this.Load += (sender, eventArgs) =>
            {
                _splitContainerMain.Panel1MinSize = 220;
                _splitContainerMain.Panel2MinSize = 400;
                _splitContainerLeft.Panel1MinSize = 150;
                _splitContainerLeft.Panel2MinSize = 100;
                _splitContainerMain.SplitterDistance = Math.Max(220, Math.Min(320, _splitContainerMain.Width - 400));
                _splitContainerLeft.SplitterDistance = Math.Max(150, Math.Min(380, _splitContainerLeft.Height - 100));
            };
        }

        private void InitializeNewDatabase()
        {
            _databaseManager?.Dispose();
            _databaseFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "proje3.db");
            bool isNewDatabase = !File.Exists(_databaseFilePath);

            _databaseManager = new DatabaseManager(_databaseFilePath);

            if (isNewDatabase)
            {
                Log($"[OK] Yeni veritabanı oluşturuldu: {_databaseFilePath}", Color.LimeGreen);
            }
            else
            {
                Log($"[OK] Mevcut veritabanı bağlandı: {_databaseFilePath}", Color.LimeGreen);
                RefreshTableTabs();
            }
        }

        private async void ButtonSelectJson_Click(object? sender, EventArgs eventArgs)
        {
            using var fileDialog = new OpenFileDialog
            {
                Title = "JSON Dosyası Seç",
                Filter = "JSON Dosyaları (*.json)|*.json|Tüm Dosyalar (*.*)|*.*"
            };
            if (fileDialog.ShowDialog() != DialogResult.OK) return;

            string selectedFilePath = fileDialog.FileName;
            _labelFileStatus.Text = Path.GetFileName(selectedFilePath);
            _buttonSelectJson.Enabled = false;
            Cursor = Cursors.WaitCursor;

            _treeViewJsonStructure.Nodes.Clear();
            try
            {
                var (rootToken, rootTreeNode) = await Task.Run(() =>
                {
                    string jsonContent = File.ReadAllText(selectedFilePath);
                    JToken parsedToken = JToken.Parse(jsonContent);
                    var treeNode = new TreeNode(Path.GetFileName(selectedFilePath)) { ImageIndex = 0 };
                    return (parsedToken, treeNode);
                });

                BuildTreeNode(rootToken, rootTreeNode);
                _treeViewJsonStructure.Nodes.Add(rootTreeNode);
                rootTreeNode.Expand();
                Log($"[OK] JSON yüklendi: {selectedFilePath}", Color.LimeGreen);
            }
            catch (Exception exception)
            {
                Log($"[HATA] JSON okuma hatası: {exception.Message}", Color.OrangeRed);
                _buttonSelectJson.Enabled = true;
                Cursor = Cursors.Default;
                return;
            }

            try
            {
                var parsingResult = await Task.Run(() => JsonParser.Parse(selectedFilePath));
                _tableSchemas = parsingResult.schemas;
                _extractedRecords = parsingResult.records;

                Log($"[OK] {_tableSchemas.Count} tablo şeması belirlendi.", Color.LimeGreen);
                _buttonConvert.Enabled = true;
                _buttonConvert.Tag = selectedFilePath;
            }
            catch (Exception exception)
            {
                Log($"[HATA] Parse hatası: {exception.Message}", Color.OrangeRed);
            }
            finally
            {
                _buttonSelectJson.Enabled = true;
                Cursor = Cursors.Default;
            }
        }

        private async void ButtonConvert_Click(object? sender, EventArgs eventArgs)
        {
            string? jsonFilePath = _buttonConvert.Tag as string;
            if (jsonFilePath == null) return;

            _buttonConvert.Enabled = false;
            _buttonSelectJson.Enabled = false;
            Cursor = Cursors.WaitCursor;
            Log("──────────────────────────────────", Color.Gray);
            Log("[BILGI] Dönüştürme başlıyor...", Color.Cyan);

            try
            {
                Log("\n[AŞAMA 1] CREATE TABLE sorguları:", Color.Yellow);
                foreach (var tableSchema in _tableSchemas)
                {
                    string createTableSql = _databaseManager.CreateTable(tableSchema);
                    Log(createTableSql, Color.White);
                }

                Log("\n[AŞAMA 2] INSERT INTO sorguları:", Color.Yellow);
                var schemaDictionary = _tableSchemas.ToDictionary(schema => schema.TableName);
                _persistedRecordLogCount = 0;

                await Task.Run(() =>
                {
                    _databaseManager.BeginTransaction();
                    try
                    {
                        foreach (var record in _extractedRecords)
                            InsertRecordRecursive(record, schemaDictionary, null);

                        _databaseManager.CommitTransaction();
                    }
                    catch
                    {
                        _databaseManager.RollbackTransaction();
                        throw;
                    }
                });

                Log("\n[AŞAMA 3] SELECT sorguları ile sonuçlar:", Color.Yellow);
                RefreshTableTabs();

                Log("\n[OK] Dönüştürme tamamlandı!", Color.LimeGreen);
            }
            catch (Exception exception)
            {
                Log($"\n[HATA] {exception.Message}", Color.OrangeRed);
                _buttonConvert.Enabled = true;
            }
            finally
            {
                _buttonSelectJson.Enabled = true;
                Cursor = Cursors.Default;
            }
        }

        private void InsertRecordRecursive(
            FlatRecord record,
            Dictionary<string, TableSchema> schemaDictionary,
            long? parentRecordId)
        {
            if (!schemaDictionary.TryGetValue(record.TableName, out var tableSchema)) return;
            var (generatedRecordId, executedInsertSql) = _databaseManager.InsertRecord(record, tableSchema, parentRecordId);

            _persistedRecordLogCount++;
            if (_persistedRecordLogCount <= 100)
            {
                Log($"  {executedInsertSql}", Color.LightGray);
            }
            else if (_persistedRecordLogCount == 101)
            {
                Log("  ... (Kalan INSERT logları arayüz performansını korumak için gizlendi) ...", Color.Gray);
            }

            foreach (var childRecord in record.Children)
                InsertRecordRecursive(childRecord, schemaDictionary, generatedRecordId);
        }

        private void RefreshTableTabs()
        {
            _tabControlTables.TabPages.Clear();
            var tableNames = _databaseManager.GetTableNames();

            foreach (var currentTableName in tableNames)
            {
                if (currentTableName.StartsWith("sqlite_")) continue;

                var countTable = _databaseManager.ExecuteQuery($"SELECT COUNT(*) FROM \"{currentTableName}\";");
                long totalRowCount = (countTable.Rows.Count > 0 && countTable.Rows[0][0] != DBNull.Value)
                    ? Convert.ToInt64(countTable.Rows[0][0])
                    : 0;

                string previewQuery = $"SELECT * FROM \"{currentTableName}\" LIMIT 500;";
                DataTable tableData = _databaseManager.ExecuteQuery(previewQuery);

                if (totalRowCount > 500)
                    Log($"  Tablo '{currentTableName}': Toplam {totalRowCount} satır (Grid üzerinde ilk 500 satır önizleniyor)", Color.LightSkyBlue);
                else
                    Log($"  Tablo '{currentTableName}': {totalRowCount} satır", Color.LightSkyBlue);

                var tableTabPage = new TabPage(currentTableName);
                var tableDataGrid = CreateStyledDataGrid();
                tableDataGrid.DataSource = tableData;
                tableTabPage.Controls.Add(tableDataGrid);
                _tabControlTables.TabPages.Add(tableTabPage);
            }

            if (_tabControlTables.TabPages.Count > 0)
                _tabControlTables.SelectedIndex = 0;
        }

        private void ButtonRunQuery_Click(object? sender, EventArgs eventArgs)
        {
            string rawQueryText = _textBoxSqlEditor.Text.Trim();
            if (string.IsNullOrEmpty(rawQueryText))
            {
                MessageBox.Show("Lütfen bir SQL sorgusu girin.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Log("──────────────────────────────────", Color.Gray);
            Log($"[BILGI] Sorgu çalıştırılıyor:\n  {rawQueryText}", Color.Cyan);

            try
            {
                string normalizedQuery = rawQueryText.TrimStart().ToUpper();
                if (normalizedQuery.StartsWith("SELECT"))
                {
                    DataTable queryResultTable = _databaseManager.ExecuteQuery(rawQueryText);
                    Log($"[OK] {queryResultTable.Rows.Count} satır döndü.", Color.LimeGreen);

                    string dynamicTabTitle = $"Sorgu ({DateTime.Now:HH:mm:ss})";
                    var queryResultTabPage = new TabPage(dynamicTabTitle);
                    var queryResultGrid = CreateStyledDataGrid();
                    queryResultGrid.DataSource = queryResultTable;
                    queryResultTabPage.Controls.Add(queryResultGrid);
                    _tabControlTables.TabPages.Add(queryResultTabPage);
                    _tabControlTables.SelectedTab = queryResultTabPage;
                }
                else
                {
                    _databaseManager.ExecuteNonQuery(rawQueryText);
                    Log("[OK] Sorgu başarıyla çalıştırıldı.", Color.LimeGreen);
                    RefreshTableTabs();
                }
            }
            catch (Exception exception)
            {
                Log($"[HATA] SQL Hatası: {exception.Message}", Color.OrangeRed);
            }
        }

        private void ButtonReset_Click(object? sender, EventArgs eventArgs)
        {
            var confirmationResult = MessageBox.Show(
                "Tüm tablolar silinecek ve veritabanı sıfırlanacak.\nDevam etmek istiyor musunuz?",
                "Sıfırla", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (confirmationResult != DialogResult.Yes) return;

            Log("──────────────────────────────────", Color.Gray);
            Log("[BILGI] Sıfırlama başlıyor...", Color.Cyan);

            try
            {
                var executedDropStatements = _databaseManager.DropAllTables();
                foreach (var dropStatement in executedDropStatements)
                    Log($"  {dropStatement}", Color.OrangeRed);

                _tabControlTables.TabPages.Clear();
                _treeViewJsonStructure.Nodes.Clear();
                _buttonConvert.Enabled = false;
                _buttonConvert.Tag = null;
                _labelFileStatus.Text = "Dosya seçilmedi";
                Log("[OK] Sıfırlama tamamlandı.", Color.LimeGreen);
            }
            catch (Exception exception)
            {
                Log($"[HATA] {exception.Message}", Color.OrangeRed);
            }
        }

        private void BuildTreeNode(JToken currentToken, TreeNode parentNode)
        {
            switch (currentToken)
            {
                case JObject jsonObject:
                    foreach (var jsonProperty in jsonObject.Properties())
                    {
                        string nodeLabel = jsonProperty.Value is JObject || jsonProperty.Value is JArray
                            ? jsonProperty.Name
                            : $"{jsonProperty.Name}: {jsonProperty.Value}";
                        var propertyNode = new TreeNode(nodeLabel);
                        parentNode.Nodes.Add(propertyNode);
                        if (jsonProperty.Value is JObject || jsonProperty.Value is JArray)
                            BuildTreeNode(jsonProperty.Value, propertyNode);
                    }
                    break;

                case JArray jsonArray:
                    for (int elementIndex = 0; elementIndex < jsonArray.Count; elementIndex++)
                    {
                        var arrayElement = jsonArray[elementIndex];
                        string elementLabel = arrayElement is JObject || arrayElement is JArray
                            ? $"[{elementIndex}]"
                            : $"[{elementIndex}]: {arrayElement}";
                        var elementNode = new TreeNode(elementLabel);
                        parentNode.Nodes.Add(elementNode);
                        if (arrayElement is JObject || arrayElement is JArray)
                            BuildTreeNode(arrayElement, elementNode);
                    }
                    break;
            }
        }

        private void Log(string message, Color messageColor)
        {
            if (_richTextBoxSqlLog.InvokeRequired)
            {
                _richTextBoxSqlLog.Invoke(() => Log(message, messageColor));
                return;
            }
            _richTextBoxSqlLog.SelectionStart = _richTextBoxSqlLog.TextLength;
            _richTextBoxSqlLog.SelectionLength = 0;
            _richTextBoxSqlLog.SelectionColor = messageColor;
            _richTextBoxSqlLog.AppendText($"{message}\n");
            _richTextBoxSqlLog.SelectionColor = _richTextBoxSqlLog.ForeColor;
            _richTextBoxSqlLog.ScrollToCaret();
        }

        private DataGridView CreateStyledDataGrid()
        {
            var dataGridView = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                ReadOnly = true,
                AllowUserToAddRows = false,
                BackgroundColor = Color.FromArgb(30, 30, 30),
                GridColor = Color.FromArgb(60, 60, 60),
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                Font = new Font("Consolas", 9f)
            };
            dataGridView.DefaultCellStyle.BackColor = Color.FromArgb(30, 30, 30);
            dataGridView.DefaultCellStyle.ForeColor = Color.White;
            dataGridView.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 120, 215);
            dataGridView.DefaultCellStyle.SelectionForeColor = Color.White;
            dataGridView.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(45, 45, 48);
            dataGridView.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(0, 180, 255);
            dataGridView.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            dataGridView.EnableHeadersVisualStyles = false;
            dataGridView.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(40, 40, 40);
            return dataGridView;
        }

        private void BuildUserInterface()
        {
            this.Text = "NoSQL → SQL Dönüştürücü";
            this.Size = new Size(1400, 800);
            this.MinimumSize = new Size(1100, 650);
            this.WindowState = FormWindowState.Maximized;
            this.BackColor = Color.FromArgb(25, 25, 25);
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 9f);
            this.StartPosition = FormStartPosition.CenterScreen;

            _panelTopControls = new Panel
            {
                Dock = DockStyle.Top,
                Height = 60,
                BackColor = Color.FromArgb(30, 30, 30),
                Padding = new Padding(10)
            };

            _labelApplicationTitle = new Label
            {
                Text = "NoSQL → SQL Dönüştürücü",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 180, 255),
                AutoSize = true,
                Location = new Point(10, 15)
            };

            _buttonSelectJson = new Button
            {
                Text = "JSON Dosyası Seç",
                Width = 190,
                Height = 36,
                Location = new Point(350, 12),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _buttonSelectJson.FlatAppearance.BorderSize = 0;
            _buttonSelectJson.Click += ButtonSelectJson_Click;

            _buttonConvert = new Button
            {
                Text = "Dönüştür",
                Width = 116,
                Height = 36,
                Location = new Point(550, 12),
                BackColor = Color.FromArgb(16, 124, 16),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            _buttonConvert.FlatAppearance.BorderSize = 0;
            _buttonConvert.Click += ButtonConvert_Click;

            _buttonResetDatabase = new Button
            {
                Text = "Sıfırla",
                Width = 95,
                Height = 36,
                Location = new Point(680, 12),
                BackColor = Color.FromArgb(180, 30, 30),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _buttonResetDatabase.FlatAppearance.BorderSize = 0;
            _buttonResetDatabase.Click += ButtonReset_Click;

            _labelFileStatus = new Label
            {
                Text = "Dosya seçilmedi",
                ForeColor = Color.Gray,
                AutoSize = true,
                Location = new Point(815, 20)
            };

            _panelTopControls.Controls.AddRange(new Control[]
            {
                _labelApplicationTitle,
                _buttonSelectJson,
                _buttonConvert,
                _buttonResetDatabase,
                _labelFileStatus
            });

            _splitContainerMain = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                BackColor = Color.FromArgb(50, 50, 50)
            };

            _splitContainerLeft = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                BackColor = Color.FromArgb(50, 50, 50)
            };

            _panelLeftContainer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 30),
                Padding = new Padding(8)
            };
            _labelJsonTreeHeader = new Label
            {
                Text = "  JSON YAPISI",
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.FromArgb(0, 180, 255),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _treeViewJsonStructure = new TreeView
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.WhiteSmoke,
                Font = new Font("Consolas", 9f),
                BorderStyle = BorderStyle.None
            };
            _panelLeftContainer.Controls.Add(_treeViewJsonStructure);
            _panelLeftContainer.Controls.Add(_labelJsonTreeHeader);

            _panelLogContainer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(20, 20, 20),
                Padding = new Padding(8)
            };
            _labelSqlLogHeader = new Label
            {
                Text = "  SQL LOG",
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.FromArgb(255, 200, 0),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _richTextBoxSqlLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(20, 20, 20),
                ForeColor = Color.LightGray,
                Font = new Font("Consolas", 8.5f),
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            _panelLogContainer.Controls.Add(_richTextBoxSqlLog);
            _panelLogContainer.Controls.Add(_labelSqlLogHeader);

            _splitContainerLeft.Panel1.Controls.Add(_panelLeftContainer);
            _splitContainerLeft.Panel2.Controls.Add(_panelLogContainer);

            _panelRightContainer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(25, 25, 25)
            };

            var panelQueryControls = new Panel
            {
                Dock = DockStyle.Top,
                Height = 70,
                BackColor = Color.FromArgb(30, 30, 30),
                Padding = new Padding(8)
            };
            _labelQueryHeader = new Label
            {
                Text = "SQL SORGUSU:",
                ForeColor = Color.FromArgb(0, 180, 255),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(8, 8)
            };
            _textBoxSqlEditor = new TextBox
            {
                Location = new Point(8, 28),
                Width = 700,
                Height = 28,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White,
                Font = new Font("Consolas", 9.5f),
                BorderStyle = BorderStyle.FixedSingle,
                Text = "SELECT * FROM sqlite_master WHERE type='table';"
            };
            _buttonExecuteQuery = new Button
            {
                Text = "Çalıştır",
                Location = new Point(718, 28),
                Width = 100,
                Height = 28,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _buttonExecuteQuery.FlatAppearance.BorderSize = 0;
            _buttonExecuteQuery.Click += ButtonRunQuery_Click;
            panelQueryControls.Controls.AddRange(new Control[]
            {
                _labelQueryHeader,
                _textBoxSqlEditor,
                _buttonExecuteQuery
            });

            _tabControlTables = new TabControl
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White
            };

            _panelRightContainer.Controls.Add(_tabControlTables);
            _panelRightContainer.Controls.Add(panelQueryControls);

            _splitContainerMain.Panel1.Controls.Add(_splitContainerLeft);
            _splitContainerMain.Panel2.Controls.Add(_panelRightContainer);

            this.Controls.Add(_splitContainerMain);
            this.Controls.Add(_panelTopControls);

            _textBoxSqlEditor.KeyDown += (sender, keyEventArgs) =>
            {
                if (keyEventArgs.KeyCode == Keys.Enter && !keyEventArgs.Shift)
                {
                    keyEventArgs.SuppressKeyPress = true;
                    ButtonRunQuery_Click(sender, keyEventArgs);
                }
            };
        }

        protected override void OnFormClosed(FormClosedEventArgs eventArgs)
        {
            _databaseManager?.Dispose();
            base.OnFormClosed(eventArgs);
        }
    }
}
