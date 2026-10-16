using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Reflection;
using System.Security.Cryptography;

class ConverterWindow : Form {
    ListView list = new ListView(); TextBox output = new TextBox(); ComboBox quality = new ComboBox();
    Label status = new Label(); Label count = new Label(); ProgressBar progress = new ProgressBar();
    Button start, stop; List<Control> controls = new List<Control>(); List<string> logs = new List<string>();
    HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    HashSet<string> formats = new HashSet<string>(new[]{".ncm",".flac",".wav",".mp3",".m4a",".aac",".ogg",".opus",".wma",".aiff",".aif",".ape"},StringComparer.OrdinalIgnoreCase);
    Process worker; string jobFile, cancelFile, runFolder; bool busy, closing, gotDone;
    Color bg = Color.FromArgb(244,246,249), ink = Color.FromArgb(29,44,66), blue = Color.FromArgb(38,101,216);
    JavaScriptSerializer json = new JavaScriptSerializer();
    public ConverterWindow() {
        Text="听转 · 批量音频转换"; ClientSize=new Size(940,680); MinimumSize=new Size(850,640);
        BackColor=bg; Font=new Font("Microsoft YaHei UI",10); StartPosition=FormStartPosition.CenterScreen;
        AutoScaleMode=AutoScaleMode.Dpi;
        AddLabel("听转  /  AUDIO CONVERTER",28,22,850,25,11,blue,true);
        AddLabel("把音乐，转成通用 MP3。",25,57,850,48,26,ink,true);
        AddLabel("NCM · FLAC · WAV · M4A · MP3 等格式   |   全程本地处理，保留原文件",28,112,850,25,10,Color.SlateGray,false);
        controls.Add(Btn("＋ 添加文件",28,157,130,delegate {using(var d=new OpenFileDialog()){d.Multiselect=true;d.Filter="支持的音频|*.ncm;*.flac;*.wav;*.mp3;*.m4a;*.aac;*.ogg;*.opus;*.wma;*.aiff;*.aif;*.ape|所有文件|*.*";if(d.ShowDialog()==DialogResult.OK)AddPaths(d.FileNames);}}));
        controls.Add(Btn("＋ 添加文件夹",168,157,145,delegate {using(var d=new FolderBrowserDialog()){d.Description="选择音频文件夹（包含子文件夹）";if(d.ShowDialog()==DialogResult.OK)AddPaths(new[]{d.SelectedPath});}}));
        controls.Add(Btn("移除选中",323,157,110,delegate {foreach(ListViewItem item in list.SelectedItems){seen.Remove((string)item.Tag);list.Items.Remove(item);}UpdateCount();}));
        controls.Add(Btn("清空",443,157,80,delegate {list.Items.Clear();seen.Clear();UpdateCount();}));
        count=AddLabel("0 个文件",720,163,190,25,10,Color.SlateGray,false);count.TextAlign=ContentAlignment.MiddleRight; count.Anchor=AnchorStyles.Top|AnchorStyles.Right;
        list.Bounds=new Rectangle(28,211,884,237);list.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;
        list.View=View.Details;list.FullRowSelect=true;list.GridLines=false;list.HideSelection=false;list.AllowDrop=true;list.BorderStyle=BorderStyle.FixedSingle;
        list.Columns.Add("文件名（可拖入文件或文件夹）",510);list.Columns.Add("格式",80);list.Columns.Add("状态",260);list.BackColor=Color.White;
        list.DoubleClick+=delegate {if(list.SelectedItems.Count>0){var i=list.SelectedItems[0];MessageBox.Show(this,i.ToolTipText.Length>0?i.ToolTipText:(string)i.Tag,"文件详情");}};
        list.ShowItemToolTips=true;Controls.Add(list);
        AllowDrop=true;DragEnter+=OnDragEnter;DragDrop+=OnDragDrop;list.DragEnter+=OnDragEnter;list.DragDrop+=OnDragDrop;
        var outputLabel=AddLabel("输出目录",28,466,85,28,10,ink,false);Bottom(outputLabel);
        output.Bounds=new Rectangle(113,465,663,29);output.Anchor=AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;
        output.Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),"听转输出");Controls.Add(output);controls.Add(output);
        var browse=Btn("选择目录",788,462,124,delegate {using(var d=new FolderBrowserDialog()){if(d.ShowDialog()==DialogResult.OK)output.Text=d.SelectedPath;}});browse.Anchor=AnchorStyles.Bottom|AnchorStyles.Right;controls.Add(browse);
        var qLabel=AddLabel("MP3 品质",28,511,85,28,10,ink,false);Bottom(qLabel);
        quality.Bounds=new Rectangle(113,509,184,30);quality.DropDownStyle=ComboBoxStyle.DropDownList;quality.Items.AddRange(new[]{"V0（高品质）","320 kbps","192 kbps","128 kbps"});quality.SelectedIndex=0;Bottom(quality);Controls.Add(quality);controls.Add(quality);
        var note=AddLabel("已有 MP3 不重复压缩；重名自动编号。输出不包含封面。",310,513,600,24,9,Color.SlateGray,false);Bottom(note);
        var open=Btn("打开输出目录",28,552,145,delegate {try{Directory.CreateDirectory(output.Text);Process.Start(new ProcessStartInfo(output.Text){UseShellExecute=true});}catch(Exception ex){Error(ex.Message);}});Bottom(open);
        var export=Btn("导出记录",183,552,115,delegate {if(logs.Count==0){MessageBox.Show(this,"还没有转换记录。");return;}using(var d=new SaveFileDialog()){d.FileName="听转记录.txt";d.Filter="文本|*.txt";if(d.ShowDialog()==DialogResult.OK){try{File.WriteAllLines(d.FileName,logs,Encoding.UTF8);}catch(Exception ex){Error(ex.Message);}}}});Bottom(export);
        stop=Btn("停止",642,552,100,delegate {Cancel();});stop.Anchor=AnchorStyles.Bottom|AnchorStyles.Right;stop.Enabled=false;
        start=Btn("开始批量转换",752,552,160,delegate {Start();});start.Anchor=AnchorStyles.Bottom|AnchorStyles.Right;start.BackColor=blue;start.ForeColor=Color.White;
        progress.Bounds=new Rectangle(28,610,884,7);progress.Anchor=AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;Controls.Add(progress);
        status=AddLabel("准备就绪 · 添加文件夹时包含子文件夹中的音频",28,631,884,27,9,Color.SlateGray,false);status.Anchor=AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;
        FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy){e.Cancel=true;if(MessageBox.Show(this,"正在转换，是否停止并退出？原文件不会改动。","停止后退出",MessageBoxButtons.YesNo)==DialogResult.Yes){closing=true;Cancel();}}};
    }
    new void Bottom(Control c){c.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;}
    Label AddLabel(string text,int x,int y,int w,int h,float size,Color color,bool bold){var l=new Label{Text=text,Bounds=new Rectangle(x,y,w,h),ForeColor=color,Font=new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular)};Controls.Add(l);return l;}
    Button Btn(string text,int x,int y,int w,EventHandler fn){var b=new Button{Text=text,Bounds=new Rectangle(x,y,w,37),FlatStyle=FlatStyle.Flat,BackColor=Color.White,Cursor=Cursors.Hand};b.FlatAppearance.BorderColor=Color.FromArgb(213,220,231);b.Click+=fn;Controls.Add(b);return b;}
    void Error(string s){MessageBox.Show(this,s,"听转",MessageBoxButtons.OK,MessageBoxIcon.Information);}
    void UpdateCount(){count.Text=list.Items.Count+" 个文件";}
    void OnDragEnter(object s,DragEventArgs e){e.Effect=!busy&&e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;}
    void OnDragDrop(object s,DragEventArgs e){if(!busy&&e.Data.GetDataPresent(DataFormats.FileDrop))AddPaths((string[])e.Data.GetData(DataFormats.FileDrop));}
    void Scan(string p,List<string> found,ref int skipped){try{if(Directory.Exists(p)){if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)return;foreach(string f in Directory.GetFiles(p))if(formats.Contains(Path.GetExtension(f)))found.Add(f);foreach(string d in Directory.GetDirectories(p))if(!Path.GetFileName(d).StartsWith(".audio-"))Scan(d,found,ref skipped);}else if(File.Exists(p)&&formats.Contains(Path.GetExtension(p)))found.Add(p);}catch(UnauthorizedAccessException){skipped++;}catch(IOException){skipped++;}}
    void AddPaths(string[] paths){if(busy)return;int skipped=0,added=0;var found=new List<string>();foreach(string p in paths)Scan(p,found,ref skipped);list.BeginUpdate();foreach(string p in found){string full=Path.GetFullPath(p);if(seen.Add(full)){var item=new ListViewItem(new[]{Path.GetFileName(full),Path.GetExtension(full).TrimStart('.').ToUpperInvariant(),"等待转换"});item.Tag=full;item.ToolTipText=full;list.Items.Add(item);added++;}}list.EndUpdate();UpdateCount();status.Text="新增 "+added+" 个文件，重复文件已跳过"+(skipped>0?"；不可访问目录 "+skipped+" 个":"");}
    string ExtractWorker(){string dir=Path.Combine(Path.GetTempPath(),"TingZhuan-1.0");Directory.CreateDirectory(dir);string path=Path.Combine(dir,"audio-worker.exe");using(Stream s=Assembly.GetExecutingAssembly().GetManifestResourceStream("audio-worker.exe")){if(s==null)throw new Exception("缺少转换引擎，请重新下载程序。");bool same=false;if(File.Exists(path)){using(var sha=SHA256.Create()){byte[] expected=sha.ComputeHash(s);s.Position=0;using(var old=File.OpenRead(path))same=sha.ComputeHash(old).SequenceEqual(expected);}}if(!same){string tmp=path+"."+Guid.NewGuid().ToString("N");using(var w=File.Create(tmp))s.CopyTo(w);if(File.Exists(path))File.Delete(path);File.Move(tmp,path);}}return path;}
    void Start(){if(busy)return;if(list.Items.Count==0){Error("请先添加音频文件或文件夹。");return;}try{if(String.IsNullOrWhiteSpace(output.Text))throw new Exception("请选择输出目录。");string dir=Path.GetFullPath(output.Text);Directory.CreateDirectory(dir);string probe=Path.Combine(dir,".write-test-"+Guid.NewGuid().ToString("N"));using(var f=File.Create(probe)){}File.Delete(probe);string engine=ExtractWorker();runFolder=Path.Combine(Path.GetTempPath(),"TingZhuan-job-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(runFolder);jobFile=Path.Combine(runFolder,"job.json");cancelFile=Path.Combine(runFolder,"cancel");var files=list.Items.Cast<ListViewItem>().Select(i=>(string)i.Tag).ToArray();var job=new Dictionary<string,object>{{"files",files},{"output",dir},{"quality",quality.Text},{"cancel",cancelFile}};File.WriteAllText(jobFile,json.Serialize(job),new UTF8Encoding(false));logs.Clear();progress.Value=0;gotDone=false;foreach(ListViewItem i in list.Items){i.SubItems[2].Text="等待转换";i.ToolTipText=(string)i.Tag;}SetBusy(true);status.Text="正在准备转换引擎，首次运行可能需要几秒…";worker=new Process();worker.StartInfo=new ProcessStartInfo(engine,"\""+jobFile+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};worker.StartInfo.StandardOutputEncoding=Encoding.UTF8;worker.StartInfo.StandardErrorEncoding=Encoding.UTF8;worker.EnableRaisingEvents=true;worker.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null)UI(delegate {Receive(e.Data);});};worker.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null)UI(delegate {logs.Add(e.Data);});};worker.Exited+=delegate{worker.WaitForExit();UI(delegate{if(!gotDone)status.Text="引擎退出，尚未完成。请导出记录查看原因。";SetBusy(false);try{Directory.Delete(runFolder,true);}catch{}if(closing)Close();});};worker.Start();worker.BeginOutputReadLine();worker.BeginErrorReadLine();}catch(Exception ex){SetBusy(false);Error(ex.Message);}}
    void UI(Action a){if(!IsDisposed&&IsHandleCreated){try{BeginInvoke(a);}catch(InvalidOperationException){}}}
    void SetBusy(bool value){busy=value;foreach(Control c in controls)c.Enabled=!value;start.Enabled=!value;stop.Enabled=value;}
    void Cancel(){if(!busy)return;try{File.WriteAllText(cancelFile,"");stop.Enabled=false;status.Text="正在停止并清理未完成文件…";}catch(Exception ex){Error(ex.Message);}}
    void Receive(string line){try{var v=json.Deserialize<Dictionary<string,object>>(line);string kind=(string)v["kind"];int idx=v.ContainsKey("index")?Convert.ToInt32(v["index"]):-1;string text=v.ContainsKey("text")?(string)v["text"]:"";if(kind=="status"&&idx>=0){list.Items[idx].SubItems[2].Text=text;status.Text="正在处理："+list.Items[idx].Text;}else if(kind=="success"||kind=="error"){var i=list.Items[idx];i.SubItems[2].Text=kind=="success"?"完成":"失败（双击查看）";i.ToolTipText=text;logs.Add((kind=="success"?"成功":"失败")+" | "+i.Tag+" | "+text);}else if(kind=="progress")progress.Value=Math.Max(0,Math.Min(100,Convert.ToInt32(v["value"])));else if(kind=="done"){gotDone=true;status.Text=((bool)v["cancelled"]?"已停止":"处理完成")+" · 成功 "+v["good"]+" 个，失败 "+v["bad"]+" 个";}else if(kind=="fatal"){logs.Add(text);status.Text="转换失败："+text;}}catch(Exception ex){logs.Add("引擎记录："+line+" | "+ex.Message);}}
    [STAThread] static void Main(string[] args){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);var f=new ConverterWindow();if(args.Length==2&&args[0]=="--verify-engine"){try{var info=new ProcessStartInfo(f.ExtractWorker(),"\""+Path.GetFullPath(args[1])+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};using(var p=Process.Start(info)){string result=p.StandardOutput.ReadToEnd();string error=p.StandardError.ReadToEnd();p.WaitForExit();File.WriteAllText(args[1]+".result.txt",result+error,Encoding.UTF8);Environment.Exit(p.ExitCode);}}catch(Exception e){File.WriteAllText(args[1]+".result.txt",e.ToString());Environment.Exit(1);}return;}if(args.Contains("--smoke-test")){var timer=new Timer{Interval=1800};timer.Tick+=delegate{timer.Stop();f.Close();};timer.Start();}Application.Run(f);}
}
