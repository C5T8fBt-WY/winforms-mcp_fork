namespace C5T8fBtWY.WinFormsMcp.TestApp;

partial class Form1
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();

        // Title label
        var titleLabel = new System.Windows.Forms.Label
        {
            Name = "titleLabel",
            Text = "WinForms MCP Test Application",
            Font = new System.Drawing.Font("Arial", 16, System.Drawing.FontStyle.Bold),
            Location = new System.Drawing.Point(10, 10),
            Size = new System.Drawing.Size(400, 30)
        };
        this.Controls.Add(titleLabel);

        // ================ TabControl ================
        this.tabControl = new System.Windows.Forms.TabControl
        {
            Name = "tabControl",
            Location = new System.Drawing.Point(10, 45),
            Size = new System.Drawing.Size(780, 300)
        };

        // ================ Tab 1: Inputs ================
        this.tabInputs = new System.Windows.Forms.TabPage
        {
            Name = "tabInputs",
            Text = "Inputs",
            Padding = new System.Windows.Forms.Padding(10)
        };

        // TextBox section
        var textBoxLabel = new System.Windows.Forms.Label
        {
            Text = "Text Input:",
            Location = new System.Drawing.Point(10, 20),
            Size = new System.Drawing.Size(100, 20)
        };
        this.tabInputs.Controls.Add(textBoxLabel);

        this.textBox = new System.Windows.Forms.TextBox
        {
            Name = "textBox",
            Location = new System.Drawing.Point(120, 20),
            Size = new System.Drawing.Size(200, 20),
            Text = "Type here..."
        };
        this.tabInputs.Controls.Add(this.textBox);

        // Button
        this.clickButton = new System.Windows.Forms.Button
        {
            Name = "clickButton",
            Text = "Click Me",
            Location = new System.Drawing.Point(10, 55),
            Size = new System.Drawing.Size(100, 30)
        };
        this.clickButton.Click += (s, e) => {
            this.statusLabel.Text = "Status: Button clicked!";
            this.statusLabel.ForeColor = System.Drawing.Color.Blue;
        };
        this.tabInputs.Controls.Add(this.clickButton);

        // Reset Button
        this.resetButton = new System.Windows.Forms.Button
        {
            Name = "resetButton",
            Text = "Reset",
            Location = new System.Drawing.Point(120, 55),
            Size = new System.Drawing.Size(80, 30)
        };
        this.resetButton.Click += (s, e) => {
            this.textBox.Text = "Type here...";
            this.checkBox.Checked = false;
            this.statusLabel.Text = "Status: Reset complete";
            this.statusLabel.ForeColor = System.Drawing.Color.Green;
        };
        this.tabInputs.Controls.Add(this.resetButton);

        // CheckBox
        this.checkBox = new System.Windows.Forms.CheckBox
        {
            Name = "checkBox",
            Text = "Enable feature",
            Location = new System.Drawing.Point(10, 100),
            Size = new System.Drawing.Size(150, 30)
        };
        this.checkBox.CheckedChanged += (s, e) => {
            var state = this.checkBox.Checked ? "enabled" : "disabled";
            this.statusLabel.Text = $"Status: Feature {state}";
        };
        this.tabInputs.Controls.Add(this.checkBox);

        // Submit Button
        this.submitButton = new System.Windows.Forms.Button
        {
            Name = "submitButton",
            Text = "Submit",
            Location = new System.Drawing.Point(10, 140),
            Size = new System.Drawing.Size(100, 35),
            BackColor = System.Drawing.Color.LightGreen
        };
        this.submitButton.Click += (s, e) => {
            this.statusLabel.Text = $"Status: Submitted '{this.textBox.Text}'";
            this.statusLabel.ForeColor = System.Drawing.Color.DarkGreen;
        };
        this.tabInputs.Controls.Add(this.submitButton);

        this.tabControl.TabPages.Add(this.tabInputs);

        // ================ Tab 2: Data ================
        this.tabData = new System.Windows.Forms.TabPage
        {
            Name = "tabData",
            Text = "Data",
            Padding = new System.Windows.Forms.Padding(10)
        };

        // ComboBox
        var comboLabel = new System.Windows.Forms.Label
        {
            Text = "Select option:",
            Location = new System.Drawing.Point(10, 20),
            Size = new System.Drawing.Size(100, 20)
        };
        this.tabData.Controls.Add(comboLabel);

        this.comboBox = new System.Windows.Forms.ComboBox
        {
            Name = "comboBox",
            Location = new System.Drawing.Point(120, 20),
            Size = new System.Drawing.Size(150, 25),
            DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        };
        this.comboBox.Items.AddRange(new object[] { "Option 1", "Option 2", "Option 3", "Option 4" });
        this.comboBox.SelectedIndexChanged += (s, e) => {
            this.statusLabel.Text = $"Status: Selected {this.comboBox.SelectedItem}";
        };
        this.tabData.Controls.Add(this.comboBox);

        // DataGridView
        var gridLabel = new System.Windows.Forms.Label
        {
            Text = "Data Table:",
            Location = new System.Drawing.Point(10, 55),
            Size = new System.Drawing.Size(100, 20)
        };
        this.tabData.Controls.Add(gridLabel);

        this.dataGridView = new System.Windows.Forms.DataGridView
        {
            Name = "dataGridView",
            Location = new System.Drawing.Point(10, 80),
            Size = new System.Drawing.Size(400, 120),
            AllowUserToAddRows = false
        };
        this.dataGridView.Columns.Add("Column1", "Name");
        this.dataGridView.Columns.Add("Column2", "Value");
        this.dataGridView.Rows.Add("Row1", "Value1");
        this.dataGridView.Rows.Add("Row2", "Value2");
        this.dataGridView.Rows.Add("Row3", "Value3");
        this.tabData.Controls.Add(this.dataGridView);

        // ListBox
        var listLabel = new System.Windows.Forms.Label
        {
            Text = "List Items:",
            Location = new System.Drawing.Point(430, 20),
            Size = new System.Drawing.Size(100, 20)
        };
        this.tabData.Controls.Add(listLabel);

        this.listBox = new System.Windows.Forms.ListBox
        {
            Name = "listBox",
            Location = new System.Drawing.Point(430, 45),
            Size = new System.Drawing.Size(200, 150)
        };
        this.listBox.Items.AddRange(new object[] { "Item 1", "Item 2", "Item 3", "Item 4", "Item 5" });
        this.listBox.SelectedIndexChanged += (s, e) => {
            if (this.listBox.SelectedItem != null)
                this.statusLabel.Text = $"Status: Selected {this.listBox.SelectedItem}";
        };
        this.tabData.Controls.Add(this.listBox);

        this.tabControl.TabPages.Add(this.tabData);

        // ================ Tab 3: Settings ================
        this.tabSettings = new System.Windows.Forms.TabPage
        {
            Name = "tabSettings",
            Text = "Settings",
            Padding = new System.Windows.Forms.Padding(10)
        };

        // GroupBox for RadioButtons
        var optionsGroup = new System.Windows.Forms.GroupBox
        {
            Name = "optionsGroup",
            Text = "Options",
            Location = new System.Drawing.Point(10, 10),
            Size = new System.Drawing.Size(200, 110)
        };

        this.radioOptionA = new System.Windows.Forms.RadioButton
        {
            Name = "radioOptionA",
            Text = "Option A",
            Location = new System.Drawing.Point(15, 25),
            Size = new System.Drawing.Size(100, 25),
            Checked = true
        };
        this.radioOptionA.CheckedChanged += (s, e) => {
            if (this.radioOptionA.Checked)
                this.statusLabel.Text = "Status: Option A selected";
        };
        optionsGroup.Controls.Add(this.radioOptionA);

        this.radioOptionB = new System.Windows.Forms.RadioButton
        {
            Name = "radioOptionB",
            Text = "Option B",
            Location = new System.Drawing.Point(15, 50),
            Size = new System.Drawing.Size(100, 25)
        };
        this.radioOptionB.CheckedChanged += (s, e) => {
            if (this.radioOptionB.Checked)
                this.statusLabel.Text = "Status: Option B selected";
        };
        optionsGroup.Controls.Add(this.radioOptionB);

        this.radioOptionC = new System.Windows.Forms.RadioButton
        {
            Name = "radioOptionC",
            Text = "Option C",
            Location = new System.Drawing.Point(15, 75),
            Size = new System.Drawing.Size(100, 25)
        };
        this.radioOptionC.CheckedChanged += (s, e) => {
            if (this.radioOptionC.Checked)
                this.statusLabel.Text = "Status: Option C selected";
        };
        optionsGroup.Controls.Add(this.radioOptionC);

        this.tabSettings.Controls.Add(optionsGroup);

        // TrackBar (slider)
        var sliderLabel = new System.Windows.Forms.Label
        {
            Name = "sliderLabel",
            Text = "Volume:",
            Location = new System.Drawing.Point(10, 135),
            Size = new System.Drawing.Size(60, 20)
        };
        this.tabSettings.Controls.Add(sliderLabel);

        this.trackBar = new System.Windows.Forms.TrackBar
        {
            Name = "trackBar",
            Location = new System.Drawing.Point(70, 130),
            Size = new System.Drawing.Size(200, 45),
            Minimum = 0,
            Maximum = 100,
            Value = 50,
            TickFrequency = 10
        };
        this.trackBar.ValueChanged += (s, e) => {
            this.sliderValueLabel.Text = $"{this.trackBar.Value}%";
            this.statusLabel.Text = $"Status: Volume set to {this.trackBar.Value}%";
        };
        this.tabSettings.Controls.Add(this.trackBar);

        this.sliderValueLabel = new System.Windows.Forms.Label
        {
            Name = "sliderValueLabel",
            Text = "50%",
            Location = new System.Drawing.Point(280, 135),
            Size = new System.Drawing.Size(50, 20)
        };
        this.tabSettings.Controls.Add(this.sliderValueLabel);

        // Toggle switches (using CheckBox with Appearance.Button)
        var toggleLabel = new System.Windows.Forms.Label
        {
            Text = "Toggles:",
            Location = new System.Drawing.Point(10, 185),
            Size = new System.Drawing.Size(60, 20)
        };
        this.tabSettings.Controls.Add(toggleLabel);

        this.toggleDarkMode = new System.Windows.Forms.CheckBox
        {
            Name = "toggleDarkMode",
            Text = "Dark Mode",
            Location = new System.Drawing.Point(70, 180),
            Size = new System.Drawing.Size(100, 30),
            Appearance = System.Windows.Forms.Appearance.Button,
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        };
        this.toggleDarkMode.CheckedChanged += (s, e) => {
            var state = this.toggleDarkMode.Checked ? "ON" : "OFF";
            this.statusLabel.Text = $"Status: Dark Mode {state}";
        };
        this.tabSettings.Controls.Add(this.toggleDarkMode);

        this.toggleNotifications = new System.Windows.Forms.CheckBox
        {
            Name = "toggleNotifications",
            Text = "Notifications",
            Location = new System.Drawing.Point(180, 180),
            Size = new System.Drawing.Size(100, 30),
            Appearance = System.Windows.Forms.Appearance.Button,
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        };
        this.toggleNotifications.CheckedChanged += (s, e) => {
            var state = this.toggleNotifications.Checked ? "ON" : "OFF";
            this.statusLabel.Text = $"Status: Notifications {state}";
        };
        this.tabSettings.Controls.Add(this.toggleNotifications);

        this.tabControl.TabPages.Add(this.tabSettings);
        this.Controls.Add(this.tabControl);

        // ================ Status Bar (outside tabs) ================
        this.statusLabel = new System.Windows.Forms.Label
        {
            Name = "statusLabel",
            Text = "Status: Ready",
            Location = new System.Drawing.Point(10, 355),
            Size = new System.Drawing.Size(400, 20),
            ForeColor = System.Drawing.Color.Green
        };
        this.Controls.Add(this.statusLabel);

        // Coordinate Test Target - for testing click coordinate calculations
        var coordTestLabel = new System.Windows.Forms.Label
        {
            Name = "coordTestLabel",
            Text = "Click target:",
            Location = new System.Drawing.Point(650, 355),
            Size = new System.Drawing.Size(80, 15),
            TextAlign = System.Drawing.ContentAlignment.MiddleRight,
            Font = new System.Drawing.Font("Arial", 8)
        };
        this.Controls.Add(coordTestLabel);

        this.coordTestTarget = new System.Windows.Forms.Panel
        {
            Name = "coordTestTarget",
            Location = new System.Drawing.Point(735, 352),
            Size = new System.Drawing.Size(20, 20),
            BackColor = System.Drawing.Color.Red,
            Cursor = System.Windows.Forms.Cursors.Hand
        };
        this.coordTestTarget.Click += (s, e) => {
            this.coordTestTarget.BackColor = System.Drawing.Color.Green;
            this.statusLabel.Text = "Status: Coordinate test target clicked!";
            this.statusLabel.ForeColor = System.Drawing.Color.Blue;
        };
        this.Controls.Add(this.coordTestTarget);

        // Form configuration
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(800, 385);
        this.Text = "WinForms MCP Test Application";
        this.Name = "TestForm";
    }

    #endregion

    // TabControl and TabPages
    private System.Windows.Forms.TabControl tabControl;
    private System.Windows.Forms.TabPage tabInputs;
    private System.Windows.Forms.TabPage tabData;
    private System.Windows.Forms.TabPage tabSettings;

    // Tab 1: Inputs
    private System.Windows.Forms.TextBox textBox;
    private System.Windows.Forms.Button clickButton;
    private System.Windows.Forms.Button resetButton;
    private System.Windows.Forms.Button submitButton;
    private System.Windows.Forms.CheckBox checkBox;

    // Tab 2: Data
    private System.Windows.Forms.ComboBox comboBox;
    private System.Windows.Forms.DataGridView dataGridView;
    private System.Windows.Forms.ListBox listBox;

    // Tab 3: Settings
    private System.Windows.Forms.RadioButton radioOptionA;
    private System.Windows.Forms.RadioButton radioOptionB;
    private System.Windows.Forms.RadioButton radioOptionC;
    private System.Windows.Forms.TrackBar trackBar;
    private System.Windows.Forms.Label sliderValueLabel;
    private System.Windows.Forms.CheckBox toggleDarkMode;
    private System.Windows.Forms.CheckBox toggleNotifications;

    // Common
    private System.Windows.Forms.Label statusLabel;
    private System.Windows.Forms.Panel coordTestTarget;
}
