using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using DesktopDrawer;

internal static class NotesCheck
{
    [STAThread] private static int Main(string[] args)
    {
        NotesController controller = null;
        Window owner = null;
        try
        {
            string root = args[0]; Directory.CreateDirectory(root);
            string data = Path.Combine(root,"records"); var store = new NoteStore(data); var note = store.Add();
            note.Title = "中文标题 😀"; note.Body = "第一行\r\n第二行\t<> & 😀"; note.Pinned = true; note.X = -900; note.Y = -100; note.Width = 450; note.Height = 300;
            store.Touch(note); Check(store.Flush(),"initial save");
            var loaded = new NoteStore(data).Items.Single();
            Check(loaded.Title == note.Title && loaded.Body == note.Body && loaded.Pinned && loaded.X == -900 && loaded.Width == 450,"Unicode, multiline and position persistence");
            note.Body = "new version"; store.Touch(note); Check(store.Flush(),"second save");
            string record = Path.Combine(data,note.Id+".note"); File.WriteAllText(record,"corrupt");
            var recovered = new NoteStore(data); var previous = recovered.Items.Single();
            Check(previous.Body.Contains("第一行") && previous.Recovered,"valid backup recovery");
            Check(recovered.Flush() && Directory.GetFiles(data,"*.corrupt-*").Length == 1,"repair preserves corrupt primary and good backup");
            previous.Deleted = true; previous.Pinned = false; recovered.Touch(previous); Check(recovered.Flush(),"trash save");
            var removed = new NoteStore(data).Items.Single(); Check(removed.Deleted && removed.Body.Contains("第二行"),"trash preserves contents");
            previous.Deleted = false; recovered.Touch(previous); Check(recovered.Flush() && !new NoteStore(data).Items.Single().Deleted,"trash restoration");
            string blocked = Path.Combine(root,"blocked"); File.WriteAllText(blocked,"block directory creation");
            var blockedStore = new NoteStore(blocked); var pending = blockedStore.Add(); pending.Body = "must survive"; blockedStore.Touch(pending);
            Check(!blockedStore.Flush() && blockedStore.HasPending,"failed save remains dirty");
            File.Delete(blocked); Check(blockedStore.Flush() && new NoteStore(blocked).Items.Single().Body == "must survive","save retry preserves input");
            string source = File.ReadAllText(args[1]);
            var baseStyle = new Style(typeof(ButtonBase));
            baseStyle.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(43,48,58))));
            baseStyle.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));
            baseStyle.Setters.Add(new Setter(Control.BorderBrushProperty,new SolidColorBrush(Color.FromRgb(71,79,93))));
            baseStyle.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(1)));
            baseStyle.Setters.Add(new Setter(Control.TemplateProperty,XamlReader.Parse(Regex.Match(source,@"<ControlTemplate[^>]+TargetType='\{x:Type ButtonBase\}'>.*?</ControlTemplate>",RegexOptions.Singleline).Value)));
            var buttons = new Style(typeof(Button),baseStyle);
            var inputs = (Style)XamlReader.Parse(Regex.Match(source,@"<Style[^>]+TargetType='\{x:Type TextBox\}'>.*?</Style>",RegexOptions.Singleline).Value);
            var scroll = (Style)XamlReader.Parse(Regex.Match(source,@"<Style[^>]+TargetType='\{x:Type ScrollBar\}'>.*?</Style>",RegexOptions.Singleline).Value);
            string uiData = Path.Combine(root,"ui"); var seed = new NoteStore(uiData);
            for(int i=0;i<8;i++){var seedNote=seed.Add();seedNote.Title="便签 "+i;seedNote.Body="正文 "+i;seedNote.Pinned=i==0;seed.Touch(seedNote);} Check(seed.Flush(),"seed cards");
            string legacyId=seed.Items[0].Id;
            owner = new Window { Width=1600,Height=340 };
            controller = new NotesController(owner,buttons,inputs,scroll,uiData); owner.Content = controller.View; controller.View.Visibility = Visibility.Visible;
            Check(!controller.Store.Items.Any(entry=>entry.Pinned),"old desktop pins migrate into drawer");
            Check(typeof(NotesController).Assembly.GetType("DesktopDrawer.NoteWindow") == null,"floating note windows removed");
            Layout(controller,1600);
            var cards=(StackPanel)Field(controller,"cards");var scroller=(ScrollViewer)Field(controller,"scroller");
            Check(cards.Children.Count==8 && scroller.ScrollableWidth>0,"all cards available horizontally");
            double five=cards.Children.Cast<Border>().Take(5).Sum(card=>card.ActualWidth)+4*12;
            Check(Math.Abs(five-scroller.ViewportWidth)<1,"five equal-width cards on wide screen");
            var horizontal=(ScrollBar)scroller.Template.FindName("PART_HorizontalScrollBar",scroller);
            Check(horizontal.ActualHeight==8,"horizontal scrollbar is slim: "+horizontal.ActualHeight);
            var track=(Track)horizontal.Template.FindName("PART_Track",horizontal);
            Check(!track.IsDirectionReversed && track.Thumb.ActualHeight<=8,"horizontal track direction and thumb fit");
            ScrollBar.PageRightCommand.Execute(null,horizontal); Drain(); controller.View.UpdateLayout();
            Check(scroller.HorizontalOffset>0,"horizontal track paging");
            scroller.ScrollToHome(); Drain(); controller.View.UpdateLayout();
            ((Button)Field(controller,"next")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Drain();controller.View.UpdateLayout();
            Check(scroller.HorizontalOffset>0,"right navigation");
            ((Button)Field(controller,"previous")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Drain();controller.View.UpdateLayout();
            Check(scroller.HorizontalOffset<1,"left navigation");
            Wheel(scroller);Drain();controller.View.UpdateLayout();
            Check(scroller.HorizontalOffset>0,"mouse wheel switches cards");
            Layout(controller,1200);
            double four=cards.Children.Cast<Border>().Take(4).Sum(card=>card.ActualWidth)+3*12;
            Check(Math.Abs(four-scroller.ViewportWidth)<1,"four equal-width cards on smaller screen");
            var editors=(Dictionary<string,NoteEditor>)Field(controller,"editors");var pane=editors[legacyId];
            var paneTitle=(TextBox)Field(pane,"title");var paneBody=(TextBox)Field(pane,"body");
            paneTitle.Text="界面便签";paneBody.Text="正文搜索词\n第二行";
            controller.SetQuery("搜索词");Check(cards.Children.Count==1,"body search");
            controller.SetQuery("does-not-exist");Check(cards.Children.Count==0,"search filters unrelated notes");
            controller.SetQuery("");Layout(controller,1600);
            pane=((Dictionary<string,NoteEditor>)Field(controller,"editors"))[legacyId];paneBody=(TextBox)Field(pane,"body");
            paneBody.Text=String.Join("\n",Enumerable.Range(0,100).Select(i=>"长便签第 "+i+" 行"));Layout(controller,1600);
            var inner=Descendants<ScrollViewer>(paneBody).First();Check(inner.ScrollableHeight>0,"long note scrolls internally");
            double outerBefore=scroller.HorizontalOffset;
            Wheel(inner);Drain();controller.View.UpdateLayout();
            Check(inner.VerticalOffset>0 && scroller.HorizontalOffset==outerBefore,"body wheel keeps reading inside long note");
            controller.SetSuspended(true);Check(!controller.View.IsEnabled,"lock suspension disables note interaction");controller.SetSuspended(false);Check(controller.View.IsEnabled,"unlock resumes notes");
            var legacy=controller.Store.Items.Single(item=>item.Id==legacyId);legacy.Deleted=true;controller.Store.Touch(legacy);
            ((ToggleButton)Field(controller,"trash")).IsChecked=true;Check(cards.Children.Count==1,"trash card");
            pane=((Dictionary<string,NoteEditor>)Field(controller,"editors"))[legacyId];Check(((TextBox)Field(pane,"body")).IsReadOnly,"trash text is read only");
            var restore=pane.View.Children.OfType<Button>().Single();Check(restore.IsEnabled,"restore stays clickable");restore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!legacy.Deleted && legacy.Body.Contains("长便签"),"restore exact card without loss");
            ((ToggleButton)Field(controller,"trash")).IsChecked=false;
            controller.SetQuery("no match");bool reset=false;controller.ResetSearch+=delegate{reset=true;};controller.NewNote();Check(reset && cards.Children.Count==9,"new note clears filter");
            Check(controller.Flush(),"UI save");controller.Dispose();controller=null;
            var persisted=new NoteStore(uiData);Check(persisted.Items.Count==9 && !persisted.Items.Any(item=>item.Pinned) && persisted.Items.Single(item=>item.Id==legacyId).Body.Contains("长便签"),"restart preserves all card contents and retired pin state");
            File.WriteAllText(Path.Combine(root,"report.txt"),"PASS: existing persistence/recovery/retry checks; old pins retained as drawer notes; floating UI removed; 5/4 equal-width cards; slim horizontal bar; track, arrows and wheel navigation; independent long-note scrolling; card search, trash and restore; restart preserves contents. Hidden layout and temporary data only; no desktop lock activated.");
            return 0;
        }
        catch(Exception error) { File.WriteAllText(Path.Combine(args[0],"report.txt"),"FAIL: "+error); return 1; }
        finally { if(controller != null)controller.Dispose(); if(owner != null)owner.Close(); }
    }
    private static object Field(object value,string name) { return value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value); }
    private static void Layout(NotesController controller,double width){controller.View.Measure(new Size(width,300));controller.View.Arrange(new Rect(0,0,width,300));controller.View.UpdateLayout();Drain();controller.View.UpdateLayout();}
    private static void Wheel(UIElement target){var e=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=Mouse.PreviewMouseWheelEvent};target.RaiseEvent(e);if(!e.Handled){e.RoutedEvent=Mouse.MouseWheelEvent;target.RaiseEvent(e);}}
    private static IEnumerable<T> Descendants<T>(DependencyObject parent)where T:DependencyObject{for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T)yield return (T)child;foreach(var nested in Descendants<T>(child))yield return nested;}}
    private static void Drain() { Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background,new Action(delegate{})); }
    private static void Check(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
}
