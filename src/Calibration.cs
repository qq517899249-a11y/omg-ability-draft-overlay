namespace OmgAd;

public sealed class CalibrationForm:Form
{
    readonly Bitmap frame;readonly Settings settings;
    readonly ListBox list=new(){Dock=DockStyle.Fill};
    readonly PictureBox preview=new(){Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,BackColor=Art.Bg};
    Point start;Rectangle selected;bool drawing;
    public CalibrationForm(Bitmap frame,Settings settings)
    {
        this.frame=frame;this.settings=settings;Text="校准识别格 · 左侧选格，右侧拖拽图标内部";Size=new Size(1250,800);StartPosition=FormStartPosition.CenterParent;
        var side=new Panel{Dock=DockStyle.Left,Width=235};Controls.Add(preview);Controls.Add(side);preview.Image=frame;
        var info=new Label{Text="先选左侧格子，再拖出对应区域。\nhero-name：仅框 ID 上方英雄名。\npool/pick：图标内部，避开边框文字。\n左侧天辉 1–5，右侧夜魇 1–5。\n保存后重新识别。",Dock=DockStyle.Top,Height=110};
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=100};
        var save=new Button{Text="保存校准",Width=100};save.Click+=(_,_)=>{settings.SaveProfile(frame.Size);UserSettings.Save(settings);DialogResult=DialogResult.OK;Close();};
        var add=new Button{Text="增加池格",Width=100};add.Click+=(_,_)=>{settings.Regions.Add(new Region{Role="pool",X=.4f,Y=.4f,W=.03f,H=.04f});RefreshList();list.SelectedIndex=settings.Regions.Count-1;};
        var remove=new Button{Text="删除此格",Width=100};remove.Click+=(_,_)=>{if(list.SelectedIndex>=0){settings.Regions.RemoveAt(list.SelectedIndex);RefreshList();}};
        buttons.Controls.AddRange([save,add,remove]);side.Controls.Add(list);side.Controls.Add(info);side.Controls.Add(buttons);
        list.SelectedIndexChanged+=(_,_)=>preview.Invalidate();
        preview.Paint+=PaintRegions;preview.MouseDown+=(_,e)=>{if(e.Button==MouseButtons.Left){start=e.Location;drawing=true;}};
        preview.MouseMove+=(_,e)=>{if(drawing){selected=Rectangle.FromLTRB(Math.Min(start.X,e.X),Math.Min(start.Y,e.Y),Math.Max(start.X,e.X),Math.Max(start.Y,e.Y));preview.Invalidate();}};
        preview.MouseUp+=(_,_)=>
        {
            drawing=false;int i=list.SelectedIndex;var image=ImageRect();
            if(i>=0&&selected.Width>=10&&selected.Height>=10&&image.Contains(selected))
            {var r=settings.Regions[i];r.X=(selected.X-image.X)/image.Width;r.Y=(selected.Y-image.Y)/image.Height;r.W=selected.Width/image.Width;r.H=selected.Height/image.Height;}
            preview.Invalidate();
        };
        RefreshList();
    }
    RectangleF ImageRect()
    {float s=Math.Min((float)preview.Width/frame.Width,(float)preview.Height/frame.Height);return new RectangleF((preview.Width-frame.Width*s)/2,(preview.Height-frame.Height*s)/2,frame.Width*s,frame.Height*s);}
    void RefreshList()
    {list.Items.Clear();for(int i=0;i<settings.Regions.Count;i++){var r=settings.Regions[i];list.Items.Add($"{i+1:D3}  {r.Role}  {(r.Seat>=0?(r.Seat<5?"天辉":"夜魇")+(r.Seat%5+1):"池")}  {(r.Slot>=0?r.Slot+1:"")}");}preview.Invalidate();}
    void PaintRegions(object? sender,PaintEventArgs e)
    {
        var image=ImageRect();for(int i=0;i<settings.Regions.Count;i++){var r=settings.Regions[i];var rect=new RectangleF(image.X+r.X*image.Width,image.Y+r.Y*image.Height,r.W*image.Width,r.H*image.Height);Art.Border(e.Graphics,rect,i==list.SelectedIndex?Color.Yellow:Color.FromArgb(140,110,235,191),i==list.SelectedIndex?3:1);}
        if(drawing)Art.Border(e.Graphics,selected,Color.Yellow,2);
    }
}
