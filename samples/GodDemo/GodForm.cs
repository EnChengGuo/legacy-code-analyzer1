using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace GodDemo
{
    public class IniFile
    {
        public string Read(string key) { return ""; }
        public void Write(string key, string v) { }
    }

    /// <summary>檢測主畫面。</summary>
    public class GodForm : Form
    {
        private TextBox txtExposure = new TextBox();
        private TextBox txtGain = new TextBox();
        private Button btnSave = new Button();
        private IniFile _ini = new IniFile();
        private double _exposure;
        private double _gain;
        private int _waferCount;   // 晶圓數
        private string _lotId;
        private int _unusedCounter;
        private string _log;
        private int _written;

        public GodForm()
        {
            btnSave.Click += new EventHandler(btnSave_Click);
        }

        private void btnSave_Click(object s, EventArgs e) { SaveSettings(); }

        public void LoadSettings()
        {
            _exposure = double.Parse(_ini.Read("Exposure"));
            _gain = double.Parse(_ini.Read("Gain"));
            UpdateUi();
        }

        public void SaveSettings()
        {
            _exposure = double.Parse(txtExposure.Text);
            txtGain.Text = _gain.ToString();
            _ini.Write("Exposure", _exposure.ToString());
            _ini.Write("Gain", _gain.ToString());
        }

        private void UpdateUi() { txtExposure.Text = _exposure.ToString(); }

        public void AddWafer() { _waferCount++; Log("add"); }
        public int GetWaferCount() { return _waferCount; }
        public void ResetWafer() { _waferCount = 0; Log("reset"); }

        public void SetLot(string id) { _lotId = id; Log("lot"); }
        public string GetLot() { return _lotId; }

        // 寫記錄(大家都會用)
        private void Log(string m) { _log += m; }

        public void Mark() { _written = 1; }

        public void Dup1(List<int> xs)
        {
            int sum = 0;
            foreach (var x in xs)
            {
                if (x > 0) { sum += x * 2; }
                else { sum -= x; }
            }
            Console.WriteLine(sum);
            int c = 0;
            for (int i = 0; i < xs.Count; i++) { c += xs[i]; }
            Console.WriteLine(c);
        }

        public void Dup2(List<int> ys)
        {
            int total = 0;
            foreach (var y in ys)
            {
                if (y > 0) { total += y * 2; }
                else { total -= y; }
            }
            Console.WriteLine(total);
            int k = 0;
            for (int j = 0; j < ys.Count; j++) { k += ys[j]; }
            Console.WriteLine(k);
        }
    }
}
