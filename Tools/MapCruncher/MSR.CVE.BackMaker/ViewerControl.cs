using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using MSR.CVE.BackMaker.ImagePipeline;
using MSR.CVE.BackMaker.MCDebug;
using System.Linq;

namespace MSR.CVE.BackMaker
{
    public class ViewerControl : UserControl, SVDisplayParams, PinDisplayIfc, PositionUpdateIfc, InvalidatableViewIfc,
        LatLonEditIfc, ViewerControlIfc, TransparencyIfc, SnapViewDisplayIfc
    {
        public interface MouseAction
        {
            void Dragged(Point diff);
            Cursor GetCursor(bool dragging);
            void OnPopup(ContextMenu menu);
        }

        public class NoAction : MouseAction
        {
            public void Dragged(Point diff)
            {
            }

            public void OnPopup(ContextMenu menu)
            {
            }

            public Cursor GetCursor(bool dragging)
            {
                return Cursors.No;
            }
        }

        public class DragImageAction : MouseAction
        {
            private readonly ViewerControl sourceViewer;

            public DragImageAction(ViewerControl sourceViewer)
            {
                this.sourceViewer = sourceViewer;
            }

            public void Dragged(Point diff)
            {
                sourceViewer.DragOnImage(diff);
            }

            public Cursor GetCursor(bool dragging)
            {
                if (!dragging)
                {
                    return Cursors.Hand;
                }

                return Cursors.Hand;
            }

            public void OnPopup(ContextMenu menu)
            {
            }
        }

        private interface TilePaintClosure : IDisposable
        {
            void PaintTile(Graphics g, Rectangle paintLocation);
        }

        private class ImagePainter : TilePaintClosure, IDisposable
        {
            private readonly ImageRef imageRef;
            private readonly Region clipRegion;

            public ImagePainter(ImageRef imageRef, Region clipRegion)
            {
                this.imageRef = imageRef;
                this.clipRegion = clipRegion;
            }

            public void PaintTile(Graphics g, Rectangle paintLocation)
            {
                if (clipRegion != null)
                {
                    g.Clip = clipRegion;
                }

                GDIBigLockedImage image;
                Monitor.Enter(image = imageRef.image);
                try
                {
                    g.DrawImage(imageRef.image.IPromiseIAmHoldingGDISLockSoPleaseGiveMeTheImage(),
                        paintLocation,
                        new Rectangle(new Point(0, 0), imageRef.image.Size),
                        GraphicsUnit.Pixel);
                }
                finally
                {
                    Monitor.Exit(image);
                }
            }

            public void Dispose()
            {
                imageRef.Dispose();
            }
        }

        private class NullPainter : TilePaintClosure, IDisposable
        {
            public void PaintTile(Graphics g, Rectangle paintLocation)
            {
            }

            public void Dispose()
            {
            }
        }

        private class MessagePainter : TilePaintClosure, IDisposable
        {
            private readonly int offsetPixels;
            private readonly string message;
            private readonly bool fillBG;

            public MessagePainter(int offsetPixels, string message, bool fillBG)
            {
                this.offsetPixels = offsetPixels;
                this.message = message;
                this.fillBG = fillBG;
            }

            public void PaintTile(Graphics g, Rectangle paintLocation)
            {
                Brush brush = new SolidBrush(Color.LightGray);
                if (fillBG)
                {
                    g.FillRectangle(brush, paintLocation);
                }

                for (float num = 0.2f; num < 1f; num += 0.6f)
                {
                    var font = new Font("Arial", 8f);
                    var pointF =
                        new PointF(paintLocation.Left + paintLocation.Width * 0.02f +
                                   offsetPixels,
                            paintLocation.Top + paintLocation.Height * num);
                    var size = g.MeasureString(message, font);
                    g.FillEllipse(new SolidBrush(Color.Wheat), new RectangleF(pointF, size));
                    g.DrawString(message, font, new SolidBrush(Color.Crimson), pointF);
                }
            }

            public void Dispose()
            {
            }
        }

        private class TileNamePainter : TilePaintClosure, IDisposable
        {
            private readonly string tileName;

            public TileNamePainter(string tileName)
            {
                this.tileName = tileName;
            }

            public void PaintTile(Graphics g, Rectangle paintLocation)
            {
                var font = new Font("Helvetica", 10f);
                var sizeF = g.MeasureString(tileName, font);
                PointF point = new Point(paintLocation.X + 20, paintLocation.Y + 8);
                float num = 5f;
                g.CompositingMode = CompositingMode.SourceOver;
                Brush brush = new SolidBrush(Color.FromArgb(40, 0, 0, 0));
                g.FillRectangle(brush,
                    new RectangleF(new PointF(point.X - num, point.Y - num),
                        new SizeF(sizeF.Width + 2f * num, sizeF.Height + 2f * num)));
                g.DrawString(tileName, font, new SolidBrush(Color.Crimson), point);
            }

            public void Dispose()
            {
            }
        }

        private class TileBoundaryPainter : TilePaintClosure, IDisposable
        {
            public void PaintTile(Graphics g, Rectangle paintLocation)
            {
                g.DrawRectangle(new Pen(Color.Crimson), paintLocation);
            }

            public void Dispose()
            {
            }
        }

        private class PaintKit
        {
            public Rectangle paintLocation;
            public List<TilePaintClosure> meatyParts = new List<TilePaintClosure>();
            public List<TilePaintClosure> annotations = new List<TilePaintClosure>();

            public PaintKit(Rectangle paintLocation)
            {
                this.paintLocation = paintLocation;
            }
        }

        private class AsyncNotifier
        {
            private readonly ViewerControl viewerControl;
            private readonly int generation;

            public AsyncNotifier(ViewerControl viewerControl)
            {
                this.viewerControl = viewerControl;
                generation = viewerControl.asyncRequestGeneration;
            }

            public void AsyncRecordComplete(AsyncRef asyncRef)
            {
                if (viewerControl.asyncRequestGeneration == generation)
                {
                    viewerControl.InvalidateView();
                }
            }
        }

        private const int EcRadius = 6;
        private const int InvertErrorRadius = 20;
        private DisplayableSourceCache baseLayer;
        private List<DisplayableSourceCache> alphaLayers = new List<DisplayableSourceCache>();
        private List<PositionAssociationView> pinList;
        private MapPositionDelegate center;
        private InterestList activeTiles;
        private UserRegionViewController userRegionViewController;
        private LatentRegionHolder latentRegionHolder;
        private Point drag_origin;
        private bool is_dragging;
        private MouseAction imminentAction = new NoAction();
        public MapDrawingOption ShowCrosshairs;
        public MapDrawingOption ShowTileBoundaries;
        public MapDrawingOption ShowTileNames;
        public MapDrawingOption ShowPushPins;
        public MapDrawingOption ShowSourceCrop;
        private Font pinFont;
        private Brush fillBrush;
        private Brush textBrush;
        private Pen outlinePen;
        private Brush errorContribBrush;
        private Pen errorContribPen;
        private Brush errorOutlierBrush;
        private Pen errorOutlierPen;
        private int tilesRequired;
        private int tilesAvailable;
        private int asyncRequestGeneration;
        private SnapViewStoreIfc snapViewStore;
        private readonly IContainer components;
        private LLZBox llzBox;
        private Button zoomOutButton;
        private Button zoomInButton;
        private TextBox creditsTextBox;
        private Button zenButton;
        private ProgressBar displayProgressBar;

        //[CompilerGenerated]
        //private static Comparison<PositionAssociationView> <>9__CachedAnonymousMethodDelegate4;

        public MapDrawingOption ShowDMS
        {
            set
            {
                llzBox.ShowDMS = value;
            }
        }

        public ViewerControl()
        {
            InitializeComponent();

            center = new NoMapPosition().NoMapPositionDelegate;
            Dock = DockStyle.Fill;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.OptimizedDoubleBuffer,
                true);
            ContextMenu = new ContextMenu();
            ContextMenu.Popup += HandlePopup;
            Layout += ViewerControl_Layout;
            zenButton.Size = new Size(0, 0);
            zenButton.KeyDown += zenButton_KeyDown;
            zenButton.KeyUp += zenButton_KeyUp;
            InitAppearance();
        }

        public void configureLLZBoxEditable()
        {
            llzBox.configureEditable(this);
        }

        public void latEdited(double newLat)
        {
            var latlon = new LatLon(newLat, center().llz.lon);
            latlon.CheckValid(GetCoordinateSystem());
            center().setPosition(new LatLonZoom(latlon, center().llz.zoom));
            center().ForceInteractiveUpdate();
        }

        public void lonEdited(double newLon)
        {
            var latlon = new LatLon(center().llz.lat, newLon);
            latlon.CheckValid(GetCoordinateSystem());
            center().setPosition(new LatLonZoom(latlon, center().llz.zoom));
            center().ForceInteractiveUpdate();
        }

        private void zenButton_KeyDown(object sender, KeyEventArgs e)
        {
            bool handled = false;
            if ((e.KeyData & Keys.Control) == Keys.Control)
            {
                handled = true;
                if (e.KeyCode == Keys.Up)
                {
                    DragOnImage(new Point(0, -1));
                }
                else
                {
                    if (e.KeyCode == Keys.Down)
                    {
                        DragOnImage(new Point(0, 1));
                    }
                    else
                    {
                        if (e.KeyCode == Keys.Left)
                        {
                            DragOnImage(new Point(-1, 0));
                        }
                        else
                        {
                            if (e.KeyCode == Keys.Right)
                            {
                                DragOnImage(new Point(1, 0));
                            }
                            else
                            {
                                handled = false;
                            }
                        }
                    }
                }
            }

            e.Handled = handled;
        }

        private void zenButton_KeyUp(object sender, KeyEventArgs e)
        {
            if (BuildConfig.theConfig.enableSnapFeatures)
            {
                if (e.KeyCode == Keys.F5)
                {
                    if ((e.KeyData & Keys.Shift) == Keys.Shift)
                    {
                        RecordSnapView();
                    }
                    else
                    {
                        RestoreSnapView();
                    }

                    e.Handled = true;
                    return;
                }

                if (e.KeyCode == Keys.F6)
                {
                    if ((e.KeyData & Keys.Shift) == Keys.Shift)
                    {
                        RecordSnapZoom();
                    }
                    else
                    {
                        RestoreSnapZoom();
                    }

                    e.Handled = true;
                }
            }
        }

        public void SetSnapViewStore(SnapViewStoreIfc snapViewStore)
        {
            this.snapViewStore = snapViewStore;
        }

        public void RecordSnapView()
        {
            snapViewStore?.Record(center().llz);
        }

        public void RestoreSnapView()
        {
            if (snapViewStore != null)
            {
                var latLonZoom = snapViewStore.Restore();
                if (latLonZoom != default)
                {
                    center().setPosition(latLonZoom);
                    center().ForceInteractiveUpdate();
                }
            }
        }

        public void RecordSnapZoom()
        {
            snapViewStore?.RecordZoom(center().llz.zoom);
        }

        public void RestoreSnapZoom()
        {
            if (snapViewStore != null)
            {
                int num = snapViewStore.RestoreZoom();
                if (num != 0)
                {
                    center().setPosition(new LatLonZoom(center().llz.latlon, num));
                    center().ForceInteractiveUpdate();
                }
            }
        }

        public void Initialize(MapPositionDelegate mpd, string LLZBoxName)
        {
            center = mpd;
            llzBox.setName(LLZBoxName);
        }

        public void SetLLZBoxLabelStyle(LLZBox.LabelStyle labelStyle)
        {
            llzBox.SetLabelStyle(labelStyle);
        }

        private void InitAppearance()
        {
            pinFont = new Font(new FontFamily("Arial"), 10f, FontStyle.Bold);
            fillBrush = new SolidBrush(Color.White);
            textBrush = new SolidBrush(Color.Red);
            outlinePen = new Pen(textBrush, 2f);
            errorContribBrush = new SolidBrush(Color.Green);
            errorContribPen = new Pen(Color.DarkGreen, 2f);
            errorOutlierBrush = new SolidBrush(Color.Blue);
            errorOutlierPen = new Pen(Color.DarkBlue, 2f);
        }

        private void ViewerControl_Layout(object sender, LayoutEventArgs e)
        {
            MakeCreditsVisible();
        }

        private void MakeCreditsVisible()
        {
            creditsTextBox.SelectionStart = creditsTextBox.Text.Length;
            creditsTextBox.SelectionLength = 0;
            creditsTextBox.ScrollToCaret();
        }

        public void ClearLayers()
        {
            baseLayer = null;
            latentRegionHolder = null;
            alphaLayers = new List<DisplayableSourceCache>();
            SetCreditString(null);
        }

        public void SetBaseLayer(IDisplayableSource tileSource)
        {
            baseLayer = new DisplayableSourceCache(tileSource);
            SetCreditString(tileSource.GetRendererCredit());
            latentRegionHolder = null;
            userRegionViewController = null;
            InvalidateView();
        }

        public void SetLatentRegionHolder(LatentRegionHolder latentRegionHolder)
        {
            this.latentRegionHolder = latentRegionHolder;
            userRegionViewController = null;
            InvalidateView();
        }

        public void SetCreditString(string credit)
        {
            if (credit == null)
            {
                creditsTextBox.Visible = false;
                return;
            }

            creditsTextBox.Visible = true;
            creditsTextBox.Text = credit;
            MakeCreditsVisible();
        }

        public MapRectangle GetBounds()
        {
            return CoordinateSystemUtilities.GetBounds(baseLayer.GetDefaultCoordinateSystem(),
                center().llz,
                Size);
        }

        public CoordinateSystemIfc GetCoordinateSystem()
        {
            return baseLayer.GetDefaultCoordinateSystem();
        }

        public void AddAlphaLayer(IDisplayableSource tileSource)
        {
            alphaLayers.Add(new DisplayableSourceCache(tileSource));
            InvalidateView();
        }

        public void RemoveAlphaLayer(IDisplayableSource tileSource)
        {
            int index = alphaLayers.FindIndex((DisplayableSourceCache dsc0) => dsc0.BackingStoreIs(tileSource));
            alphaLayers.RemoveAt(index);
            InvalidateView();
        }

        public void setPinList(List<PositionAssociationView> newList)
        {
            pinList = newList;
            Invalidate();
        }

        private void zoomOutButton_Click(object sender, EventArgs e)
        {
            zoom(-1);
        }

        private void zoomInButton_Click(object sender, EventArgs e)
        {
            zoom(1);
        }

        public void zoom(int zoomFactor)
        {
            if (baseLayer != null)
            {
                center()
                    .setPosition(CoordinateSystemUtilities.GetZoomedView(GetCoordinateSystem(),
                        center().llz,
                        zoomFactor));
            }
        }

        private MouseAction ImminentAction(MouseEventArgs e)
        {
            MouseAction mouseAction = null;
            if (userRegionViewController != null)
            {
                mouseAction = userRegionViewController.ImminentAction(e);
            }

            if (mouseAction == null)
            {
                mouseAction = new DragImageAction(this);
            }

            return mouseAction;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            zenButton.Focus();
            base.OnMouseClick(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            D.Say(3, string.Format("Zooming -- mousedelta={0}", e.Delta));
            zoom(e.Delta / 120);
            base.OnMouseWheel(e);
        }

        protected void HandlePopup(object sender, EventArgs e)
        {
            ContextMenu.MenuItems.Clear();
            imminentAction.OnPopup(ContextMenu);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            is_dragging = true;
            drag_origin = new Point(e.X, e.Y);
            imminentAction = ImminentAction(e);
            Cursor.Current = imminentAction.GetCursor(true);
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            is_dragging = false;
            imminentAction = ImminentAction(e);
            Cursor.Current = imminentAction.GetCursor(false);
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            is_dragging = false;
            imminentAction = new NoAction();
            base.OnMouseLeave(e);
        }

        private void DragOnImage(Point diff)
        {
            center().setPosition(GetCoordinateSystem().GetTranslationInLatLon(center().llz, diff));
            center().ForceInteractiveUpdate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (baseLayer != null)
            {
                if (is_dragging)
                {
                    var diff = new Point(e.X - drag_origin.X, e.Y - drag_origin.Y);
                    imminentAction.Dragged(diff);
                    Invalidate();
                    drag_origin = new Point(e.X, e.Y);
                }
                else
                {
                    imminentAction = ImminentAction(e);
                    Cursor.Current = imminentAction.GetCursor(false);
                }
            }

            base.OnMouseMove(e);
        }

        public Point ScreenCenter()
        {
            return new Point(Size.Width / 2, Size.Height / 2);
        }

        public LatLonZoom MapCenter()
        {
            return center().llz;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (baseLayer != null)
            {
                var point = ScreenCenter();
                var offsetInPixels = new Point(point.X - e.Location.X, point.Y - e.Location.Y);
                center().setPosition(CoordinateSystemUtilities.GetZoomedView(GetCoordinateSystem(),
                    GetCoordinateSystem().GetTranslationInLatLon(center().llz, offsetInPixels),
                    1));
            }

            base.OnMouseDoubleClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            bool handled = false;
            if ((e.KeyData & Keys.Control) == Keys.Control)
            {
                handled = true;
                if (e.KeyCode == Keys.Up)
                {
                    DragOnImage(new Point(0, -1));
                }
                else
                {
                    if (e.KeyCode == Keys.Down)
                    {
                        DragOnImage(new Point(0, 1));
                    }
                    else
                    {
                        if (e.KeyCode == Keys.Left)
                        {
                            DragOnImage(new Point(-1, 0));
                        }
                        else
                        {
                            if (e.KeyCode == Keys.Right)
                            {
                                DragOnImage(new Point(1, 0));
                            }
                            else
                            {
                                handled = false;
                            }
                        }
                    }
                }
            }

            e.Handled = handled;
            base.OnKeyDown(e);
        }

        public ImageRef MessageImage(string message, Size tileSize)
        {
            var gDIBigLockedImage = new GDIBigLockedImage(tileSize, "ViewerControl-MessageImage");
            var graphics = gDIBigLockedImage.IPromiseIAmHoldingGDISLockSoPleaseGiveMeTheGraphics();
            Brush brush = new SolidBrush(Color.LightGray);
            graphics.FillRectangle(brush, 0, 0, tileSize.Width, tileSize.Height);
            graphics.DrawString(message,
                new Font("Arial", 10f),
                new SolidBrush(Color.Crimson),
                new PointF(tileSize.Width * 0.02f, tileSize.Height * 0.2f));
            graphics.DrawString(message,
                new Font("Arial", 10f),
                new SolidBrush(Color.Crimson),
                new PointF(tileSize.Width * 0.02f, tileSize.Height * 0.8f));
            return new ImageRef(new ImageRefCounted(gDIBigLockedImage));
        }

        private PointF MapPositionToPoint(LatLon pos)
        {
            var translationInPixels = GetCoordinateSystem().GetTranslationInPixels(center().llz, pos);
            var result = new PointF(Width / 2 + translationInPixels.X,
                Height / 2 + translationInPixels.Y);
            return result;
        }

        private void DrawMarker(PositionAssociationView pav, PaintSpecification e)
        {
            DrawErrorMarkers(pav, e);
            e.Graphics.CompositingMode = CompositingMode.SourceOver;
            string text = pav.pinId.ToString();
            outlinePen.MiterLimit = 2f;
            var size = new SizeF(3f, 3f);
            var pointF = MapPositionToPoint(pav.position.pinPosition.latlon);
            if (!RectangleF.Inflate(e.ClipRectangle, 100f, 100f).Contains(pointF))
            {
                return;
            }

            var sizeF = e.Graphics.MeasureString(text, pinFont);
            double num = 24.0;
            double num2 = 3.0;
            int num3 = 3;
            var layoutRectangle = new RectangleF(pointF.X - sizeF.Width / 2f,
                (float)(pointF.Y - sizeF.Height / 2f - num),
                sizeF.Width,
                sizeF.Height);
            var rectangleF = new RectangleF(layoutRectangle.Location, layoutRectangle.Size);
            rectangleF.Inflate(size);
            var points = new[]
            {
                pointF, new PointF((float)(pointF.X - num2), rectangleF.Bottom),
                new PointF(rectangleF.Left + num3, rectangleF.Bottom),
                new PointF(rectangleF.Left, rectangleF.Bottom - num3),
                new PointF(rectangleF.Left, rectangleF.Top + num3),
                new PointF(rectangleF.Left + num3, rectangleF.Top),
                new PointF(rectangleF.Right - num3, rectangleF.Top),
                new PointF(rectangleF.Right, rectangleF.Top + num3),
                new PointF(rectangleF.Right, rectangleF.Bottom - num3),
                new PointF(rectangleF.Right - num3, rectangleF.Bottom),
                new PointF((float)(pointF.X + num2), rectangleF.Bottom)
            };
            e.Graphics.FillPolygon(fillBrush, points);
            e.Graphics.DrawPolygon(outlinePen, points);
            e.Graphics.DrawString(text, pinFont, textBrush, layoutRectangle);
        }

        private void DrawErrorMarkers(PositionAssociationView pav, PaintSpecification e)
        {
            DrawErrorPosition(pav,
                DisplayablePosition.ErrorMarker.AsContributor,
                errorContribPen,
                errorContribBrush,
                e);
            DrawErrorPosition(pav,
                DisplayablePosition.ErrorMarker.AsOutlier,
                errorOutlierPen,
                errorOutlierBrush,
                e);
        }

        private void DrawErrorPosition(PositionAssociationView pav, DisplayablePosition.ErrorMarker errorMarker,
            Pen pen, Brush brush, PaintSpecification e)
        {
            var errorPosition = pav.position.GetErrorPosition(errorMarker);
            if (errorPosition == null)
            {
                return;
            }

            var pointF = MapPositionToPoint(pav.position.pinPosition.latlon);
            var pointF2 = MapPositionToPoint(errorPosition.latlon);
            var rectangleF = new RectangleF(e.ClipRectangle.X - e.ClipRectangle.Width * 2,
                e.ClipRectangle.Y - e.ClipRectangle.Height * 2,
                e.ClipRectangle.Width * 5,
                e.ClipRectangle.Height * 5);
            if (!rectangleF.Contains(pointF) || !rectangleF.Contains(pointF2))
            {
                return;
            }

            if (!pav.position.invertError)
            {
                e.Graphics.DrawLine(pen, pointF, pointF2);
                e.Graphics.FillEllipse(brush, pointF2.X - 6f, pointF2.Y - 6f, 12f, 12f);
                return;
            }

            e.Graphics.DrawEllipse(pen, pointF.X - 20f, pointF.Y - 20f, 40f, 40f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (D.CustomPaintDisabled())
            {
                return;
            }

            PaintGraphics(new PaintSpecification(e.Graphics, e.ClipRectangle, Size, false),
                center().llz);
        }

        public void PaintPrintWindow(PaintSpecification e, int extraZoom)
        {
            PaintGraphics(e,
                new LatLonZoom(center().llz.lat, center().llz.lon, center().llz.zoom + extraZoom));
        }

        private void PaintGraphics(PaintSpecification e, LatLonZoom llz)
        {
            tilesRequired = 0;
            tilesAvailable = 0;
            asyncRequestGeneration++;
            if (baseLayer == null)
            {
                return;
            }

            var interestList = activeTiles;
            activeTiles = new InterestList();
            e.ResetClip();
            e.Graphics.FillRectangle(new SolidBrush(Color.LightPink), new Rectangle(new Point(0, 0), e.Size));
            var list = new List<PaintKit>();
            list.AddRange(AssembleLayer(e, llz, baseLayer, 0));
            int num = 1;
            foreach (IDisplayableSource current in alphaLayers)
            {
                list.AddRange(AssembleLayer(e, llz, current, num));
                num++;
            }

            activeTiles.Activate();
            PaintKits(e.Graphics, list);
            e.ResetClip();
            if (userRegionViewController != null)
            {
                e.ResetClip();
                userRegionViewController.Paint(e, llz, Size);
            }

            if (MapDrawingOption.IsEnabled(ShowCrosshairs))
            {
                var pen = new Pen(Color.Yellow);
                var array = new[] {pen, new Pen(Color.Black) {DashStyle = DashStyle.Dash}};
                for (int i = 0; i < array.Length; i++)
                {
                    var pen2 = array[i];
                    e.Graphics.DrawLine(pen2, 0, Size.Height / 2, Size.Width, Size.Height / 2);
                    e.Graphics.DrawLine(pen2, Size.Width / 2, 0, Size.Width / 2, Size.Height);
                }
            }

            if (MapDrawingOption.IsEnabled(ShowPushPins) && pinList != null)
            {
                var list2 = new List<PositionAssociationView>();
                list2.AddRange(pinList);
                list2.Sort(delegate(PositionAssociationView p0, PositionAssociationView p1)
                {
                    double num2 = p1.position.pinPosition.lat - p0.position.pinPosition.lat;
                    if (num2 != 0.0)
                    {
                        if (num2 <= 0.0)
                        {
                            return -1;
                        }

                        return 1;
                    }
                    else
                    {
                        double num3 = p1.position.pinPosition.lon - p0.position.pinPosition.lon;
                        if (num3 == 0.0)
                        {
                            return 0;
                        }

                        if (num3 <= 0.0)
                        {
                            return -1;
                        }

                        return 1;
                    }
                });
                foreach (var current2 in list2)
                {
                    DrawMarker(current2, e);
                }
            }

            interestList?.Dispose();

            if (tilesRequired == 0 || tilesAvailable == tilesRequired)
            {
                displayProgressBar.Visible = false;
                return;
            }

            displayProgressBar.Visible = true;
            displayProgressBar.Minimum = 0;
            displayProgressBar.Maximum = tilesRequired;
            displayProgressBar.Value = tilesAvailable;
        }

        private void PaintKits(Graphics g, List<PaintKit> kits)
        {
            g.CompositingMode = CompositingMode.SourceOver;
            foreach (var (current, current2) in kits.SelectMany(current => current.meatyParts.Select(current2 => (current, current2))))
            {
                current2.PaintTile(g, current.paintLocation);
                current2.Dispose();
            }

            g.ResetClip();
            foreach (var (current3, current4) in kits.SelectMany(current3 => current3.annotations.Select(current4 => (current3, current4))))
            {
                current4.PaintTile(g, current3.paintLocation);
                current4.Dispose();
            }
        }

        private List<PaintKit> AssembleLayer(PaintSpecification e, LatLonZoom llz, IDisplayableSource tileSource,
            int stackOrder)
        {
            var list = new List<PaintKit>();
            var defaultCoordinateSystem = tileSource.GetDefaultCoordinateSystem();
            var tileArrayDescriptor =
                defaultCoordinateSystem.GetTileArrayDescriptor(llz, e.Size);
            AsyncRef asyncRef;
            try
            {
                asyncRef = (AsyncRef)tileSource.GetUserBounds(null, (FutureFeatures)7)
                    .Realize("ViewerControl.PaintLayer boundsRef");
            }
            catch (Exception ex)
            {
                var item = new MessagePainter(stackOrder * 12,
                    BigDebugKnob.theKnob.debugFeaturesEnabled ? ex.ToString() : "X",
                    stackOrder == 0);
                list.AddRange(tileArrayDescriptor.Select(current => new PaintKit(current.paintLocation) { annotations = { item } }));
                return list;
            }

            Region clipRegion = null;
            if (asyncRef.present == null)
            {
                asyncRef.AddCallback(BoundsRefAvailable);
                asyncRef.SetInterest(524290);
            }

            if ((ShowSourceCrop == null || ShowSourceCrop.Enabled) && asyncRef.present is IBoundsProvider provider)
            {
                clipRegion = provider.GetRenderRegion().GetClipRegion(
                    defaultCoordinateSystem.GetUnclippedMapWindow(center().llz, e.Size),
                    center().llz.zoom,
                    defaultCoordinateSystem);
                UpdateUserRegion();
            }

            new PersistentInterest(asyncRef);
            int num = 0;
            foreach (var current2 in tileArrayDescriptor)
            {
                var paintKit = new PaintKit(current2.paintLocation);
                D.Sayf(10, "count {0} tdd {1}", new object[] {num, current2.tileAddress});
                num++;
                if (e.SynchronousTiles)
                {
                    D.Sayf(0,
                        "PaintLayer({0}, tdd.ta={1})",
                        new object[] {tileSource.GetHashCode(), current2.tileAddress});
                }

                bool arg_1F5_0 = e.SynchronousTiles;
                var present = tileSource.GetImagePrototype(null, (FutureFeatures)15)
                    .Curry(new ParamDict(new object[] {TermName.TileAddress, current2.tileAddress}))
                    .Realize("ViewerControl.PaintLayer imageAsyncRef");
                var asyncRef2 = (AsyncRef)present;
                var rectangle = Rectangle.Intersect(e.ClipRectangle, current2.paintLocation);
                int interest = rectangle.Height * rectangle.Width + 524296;
                asyncRef2.SetInterest(interest);
                if (asyncRef2.present == null)
                {
                    var @object = new AsyncNotifier(this);
                    asyncRef2.AddCallback(@object.AsyncRecordComplete);
                }

                activeTiles.Add(asyncRef2);
                asyncRef2 = (AsyncRef)asyncRef2.Duplicate("ViewerControl.PaintLayer");
                if (e.SynchronousTiles)
                {
                    D.Assert(false, "unimpl");
                }

                if (asyncRef2.present == null)
                {
                    D.Assert(!e.SynchronousTiles);
                }

                bool flag;
                if (asyncRef2.present != null && asyncRef2.present is ImageRef)
                {
                    flag = false;
                    var imageRef = (ImageRef)asyncRef2.present.Duplicate("tpc");
                    paintKit.meatyParts.Add(new ImagePainter(imageRef, clipRegion));
                }
                else
                {
                    if (asyncRef2.present != null && asyncRef2.present is BeyondImageBounds)
                    {
                        flag = false;
                    }
                    else
                    {
                        if (asyncRef2.present != null && asyncRef2.present is PresentFailureCode code)
                        {
                            flag = false;
                            var presentFailureCode = code;
                            var item2 = new MessagePainter(stackOrder * 12,
                                BigDebugKnob.theKnob.debugFeaturesEnabled
                                    ? StringUtils.breakLines(presentFailureCode.ToString())
                                    : "X",
                                stackOrder == 0);
                            paintKit.annotations.Add(item2);
                        }
                        else
                        {
                            flag = true;
                            var item3 =
                                new MessagePainter(stackOrder * 12, stackOrder.ToString(), stackOrder == 0);
                            if (stackOrder == 0)
                            {
                                paintKit.meatyParts.Add(item3);
                            }
                            else
                            {
                                paintKit.annotations.Add(item3);
                            }
                        }
                    }
                }

                tilesRequired++;
                if (!flag)
                {
                    tilesAvailable++;
                }

                if (flag && stackOrder == 0 || MapDrawingOption.IsEnabled(ShowTileBoundaries))
                {
                    paintKit.annotations.Add(new TileBoundaryPainter());
                }

                if (MapDrawingOption.IsEnabled(ShowTileNames))
                {
                    paintKit.annotations.Add(new TileNamePainter(current2.tileAddress.ToString()));
                }

                asyncRef2.Dispose();
                list.Add(paintKit);
            }

            return list;
        }

        private void BoundsRefAvailable(AsyncRef asyncRef)
        {
            Invalidate();
        }

        private void UpdateUserRegion()
        {
            if (userRegionViewController == null && latentRegionHolder != null)
            {
                userRegionViewController = new UserRegionViewController(GetCoordinateSystem(),
                    this,
                    latentRegionHolder,
                    baseLayer);
                InvalidateView();
            }
        }

        public void PositionUpdated(LatLonZoom llz)
        {
            llzBox.PositionChanged(llz);
            Invalidate();
        }

        public void ForceInteractiveUpdate()
        {
            Update();
        }

        public void InvalidateView()
        {
            Invalidate();
        }

        public void InvalidatePipeline()
        {
            try
            {
                foreach (var current in alphaLayers)
                {
                    current.Flush();
                }

                baseLayer?.Flush();

                Invalidate();
            }
            catch (InvalidOperationException)
            {
            }
        }

        public void AddLayer(IDisplayableSource warpedMapTileSource)
        {
            if (baseLayer == null)
            {
                SetBaseLayer(warpedMapTileSource);
                return;
            }

            AddAlphaLayer(warpedMapTileSource);
        }

        public Pixel GetBaseLayerCenterPixel()
        {
            IDisplayableSource displayableSource = baseLayer;
            var defaultCoordinateSystem = displayableSource.GetDefaultCoordinateSystem();
            var tileArrayDescriptor =
                defaultCoordinateSystem.GetTileArrayDescriptor(center().llz, Size);
            int num = Size.Width / 2;
            int num2 = Size.Height / 2;
            foreach (var current in tileArrayDescriptor)
            {
                if (current.paintLocation.Left > num || current.paintLocation.Right <= num || current.paintLocation.Top > num2 || current.paintLocation.Bottom <= num2)
                {
                    continue;
                }

                int arg_D1_0 = num;
                int x = arg_D1_0 - current.paintLocation.Left;
                int arg_E6_0 = num2;
                int y = arg_E6_0 - current.paintLocation.Top;
                var present = displayableSource.GetImagePrototype(null, (FutureFeatures)19)
                    .Curry(new ParamDict(new object[] { TermName.TileAddress, current.tileAddress }))
                    .Realize("ViewerControl.GetBaseLayerCenterPixel imageRef");
                Pixel result;
                if (!(present is ImageRef))
                {
                    result = new UndefinedPixel();
                    return result;
                }

                var imageRef = (ImageRef)present;
                GDIBigLockedImage image;
                Monitor.Enter(image = imageRef.image);
                Pixel pixel2;

                try
                {
                    var image2 = imageRef.image.IPromiseIAmHoldingGDISLockSoPleaseGiveMeTheImage();
                    var bitmap = (Bitmap)image2;
                    var pixel = bitmap.GetPixel(x, y);
                    pixel2 = new Pixel(pixel);
                }
                finally
                {
                    Monitor.Exit(image);
                }

                imageRef.Dispose();
                result = pixel2;
                return result;
            }

            return new UndefinedPixel();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
            {
                components.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            zoomOutButton = new Button();
            zoomInButton = new Button();
            creditsTextBox = new TextBox();
            zenButton = new Button();
            displayProgressBar = new ProgressBar();
            llzBox = new LLZBox();
            SuspendLayout();
            // 
            // zoomOutButton
            // 
            zoomOutButton.Location = new Point(242, 39);
            zoomOutButton.Name = "zoomOutButton";
            zoomOutButton.Size = new Size(82, 23);
            zoomOutButton.TabIndex = 9;
            zoomOutButton.Text = "Zoom Out";
            zoomOutButton.Click += zoomOutButton_Click;
            // 
            // zoomInButton
            // 
            zoomInButton.Location = new Point(242, 68);
            zoomInButton.Name = "zoomInButton";
            zoomInButton.Size = new Size(82, 23);
            zoomInButton.TabIndex = 10;
            zoomInButton.Text = "Zoom In";
            zoomInButton.Click += zoomInButton_Click;
            // 
            // creditsTextBox
            // 
            creditsTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            creditsTextBox.Location = new Point(330, 34);
            creditsTextBox.Multiline = true;
            creditsTextBox.Name = "creditsTextBox";
            creditsTextBox.ReadOnly = true;
            creditsTextBox.Size = new Size(115, 32);
            creditsTextBox.TabIndex = 12;
            creditsTextBox.Visible = false;
            // 
            // zenButton
            // 
            zenButton.Location = new Point(242, 10);
            zenButton.Name = "zenButton";
            zenButton.Size = new Size(82, 23);
            zenButton.TabIndex = 13;
            zenButton.Text = "zenButton";
            zenButton.UseVisualStyleBackColor = true;
            // 
            // displayProgressBar
            // 
            displayProgressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left
                                                         | AnchorStyles.Right;
            displayProgressBar.Location = new Point(330, 14);
            displayProgressBar.Name = "displayProgressBar";
            displayProgressBar.Size = new Size(115, 19);
            displayProgressBar.TabIndex = 14;
            // 
            // llzBox
            // 
            llzBox.Dock = DockStyle.Top;
            llzBox.Location = new Point(0, 0);
            llzBox.Margin = new Padding(4, 4, 4, 4);
            llzBox.Name = "llzBox";
            llzBox.Size = new Size(456, 95);
            llzBox.TabIndex = 11;
            // 
            // ViewerControl
            // 
            Controls.Add(creditsTextBox);
            Controls.Add(zoomInButton);
            Controls.Add(zoomOutButton);
            Controls.Add(displayProgressBar);
            Controls.Add(zenButton);
            Controls.Add(llzBox);
            Name = "ViewerControl";
            Size = new Size(456, 615);
            ResumeLayout(false);
            PerformLayout();
        }

        Size ViewerControlIfcSize
        {
            get
            {
                return Size;
            }
        }
    }
}
