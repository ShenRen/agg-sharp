﻿/*
Copyright (c) 2014, Lars Brubaker
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met: 

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer. 
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution. 

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies, 
either expressed or implied, of the FreeBSD Project.
*/

using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Diagnostics;

using MatterHackers.Agg;
using MatterHackers.Agg.UI;
using MatterHackers.Agg.Transform;
using MatterHackers.Agg.VertexSource;
using MatterHackers.VectorMath;
using MatterHackers.Agg.Image;
using MatterHackers.Agg.RasterizerScanline;

namespace MatterHackers.GCodeVisualizer
{
    public class GCodeViewWidget : GuiWidget
    {
        public EventHandler DoneLoading;

        public ProgressChangedEventHandler LoadingProgressChanged;

        bool renderGrid = true;
        public bool RenderGrid 
        {
            get { return renderGrid; }
            set
            {
                if (renderGrid != value)
                {
                    renderGrid = value;
                    Invalidate();
                }
            }
        }

        public enum ETransformState { Move, Scale };

        public ETransformState TransformState { get; set; }

        bool renderMoves = true;
        public bool RenderMoves
        {
            get { return renderMoves; }
            set
            {
                if (renderMoves != value)
                {
                    renderMoves = value;
                    Invalidate();
                }
            }
        }

        bool renderRetractions = true;
        public bool RenderRetractions
        {
            get { return renderRetractions; }
            set
            {
                if (renderRetractions != value)
                {
                    renderRetractions = value;
                    Invalidate();
                }
            }
        }

        BackgroundWorker backgroundWorker = null;
        Vector2 lastMousePosition = new Vector2(0, 0);
        Vector2 mouseDownPosition = new Vector2(0, 0);

        double layerScale = 1;
        int activeLayerIndex;
        Vector2 gridSizeMm;
        Vector2 gridCenterMm;
        Affine ScallingTransform
        {
            get
            {
                return Affine.NewScaling(layerScale, layerScale);
            }
        }

        Affine TotalTransform
        {
            get
            {
                Affine transform = Affine.NewIdentity();
                transform *= Affine.NewTranslation(unscaledRenderOffset);

                // scale to view 
                transform *= ScallingTransform;
                transform *= Affine.NewTranslation(Width / 2, Height / 2);

                return transform;
            }
        }

        Vector2 unscaledRenderOffset = new Vector2(0, 0);

        public string FileNameAndPath;
        public GCodeFile loadedGCode;
        public GCodeRenderer gCodeRenderer;

        public event EventHandler ActiveLayerChanged;

        public GCodeFile LoadedGCode
        {
            get
            {
                return loadedGCode;
            }
        }

        public int ActiveLayerIndex
        {
            get
            {
                return activeLayerIndex;
            }

            set
            {
                if (activeLayerIndex != value)
                {
                    activeLayerIndex = value;

                    if (gCodeRenderer == null || activeLayerIndex < 0)
                    {
                        activeLayerIndex = 0;
                    }
                    else if (activeLayerIndex >= loadedGCode.NumChangesInZ)
                    {
                        activeLayerIndex = loadedGCode.NumChangesInZ - 1;
                    }
                    Invalidate();

                    if (ActiveLayerChanged != null)
                    {
                        ActiveLayerChanged(this, null);
                    }
                }
            }
        }

        public GCodeViewWidget(Vector2 gridSizeMm, Vector2 gridCenterMm)
        {
            this.gridSizeMm = gridSizeMm;
            this.gridCenterMm = gridCenterMm;
            LocalBounds = new RectangleDouble(0, 0, 100, 100);
            DoubleBuffer = true;
            AnchorAll();
        }

        public void SetGCodeAfterLoad(GCodeFile loadedGCode)
        {
            this.loadedGCode = loadedGCode;
            if (loadedGCode == null)
            {
                TextWidget noGCodeLoaded = new TextWidget(string.Format("Not a valid GCode file."));
                noGCodeLoaded.Margin = new BorderDouble(0, 0, 0, 0);
                noGCodeLoaded.VAnchor = Agg.UI.VAnchor.ParentCenter;
                noGCodeLoaded.HAnchor = Agg.UI.HAnchor.ParentCenter;
                this.AddChild(noGCodeLoaded);
            }
            else
            {
                SetInitalLayer();
                CenterPartInView();
            }
        }

        void SetInitalLayer()
        {
            activeLayerIndex = 0;
            if (loadedGCode.GCodeCommandQueue.Count > 0)
            {
                int firstExtrusionIndex = 0;
                Vector3 lastPosition = loadedGCode.GCodeCommandQueue[0].Position;
                double ePosition = loadedGCode.GCodeCommandQueue[0].EPosition;
                // let's find the first layer that has extrusion if possible and go to that
                for (int i = 1; i < loadedGCode.GCodeCommandQueue.Count; i++)
                {
                    PrinterMachineInstruction currentInstruction = loadedGCode.GCodeCommandQueue[i];
                    if (currentInstruction.EPosition > ePosition && lastPosition != currentInstruction.Position)
                    {
                        firstExtrusionIndex = i;
                        break;
                    }

                    lastPosition = currentInstruction.Position;
                }

                if (firstExtrusionIndex > 0)
                {
                    for (int i = 0; i < loadedGCode.NumChangesInZ; i++)
                    {
                        if (firstExtrusionIndex < loadedGCode.IndexOfChangeInZ[i])
                        {
                            activeLayerIndex = Math.Max(0, i-1);
                            break;
                        }
                    }
                }
            }
        }

        void backgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            SetGCodeAfterLoad((GCodeFile)e.Result);

            if(DoneLoading != null)
            {
                DoneLoading(this, null);
            }
        }

        void backgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (LoadingProgressChanged != null)
            {
                LoadingProgressChanged(this, e);
            }
        }

        PathStorage grid = new PathStorage();
        public override void OnDraw(Graphics2D graphics2D)
        {
            if (loadedGCode != null)
            {
                Affine transform = TotalTransform;

                CreateGrid(transform);

                double gridLineWidths = 0.2 * layerScale;
                Stroke stroke = new Stroke(grid, gridLineWidths);

                if (RenderGrid)
                {
                    graphics2D.Render(stroke, RGBA_Bytes.LightGray);
                }

                GCodeRenderer.RenderType renderType = GCodeRenderer.RenderType.Extrusions;
                if (RenderMoves)
                {
                    renderType |= GCodeRenderer.RenderType.Moves;
                }
                if (RenderRetractions)
                {
                    renderType |= GCodeRenderer.RenderType.Retractions;
                }

                gCodeRenderer.Render(graphics2D, activeLayerIndex, transform, layerScale, renderType);
            }

            base.OnDraw(graphics2D);
        }

        public void CreateGrid(Affine transform)
        {
            Vector2 gridOffset = gridCenterMm - gridSizeMm / 2;
            if (gridSizeMm.x > 0 && gridSizeMm.y > 0)
            {
                grid.remove_all();
                for (int y = 0; y <= gridSizeMm.y; y += 10)
                {
                    Vector2 start = new Vector2(0, y) + gridOffset;
                    Vector2 end = new Vector2(gridSizeMm.x, y) + gridOffset;
                    transform.transform(ref start);
                    transform.transform(ref end);
                    grid.MoveTo((int)(start.x + .5), (int)(start.y + .5) + .5);
                    grid.LineTo((int)(int)(end.x + .5), (int)(end.y + .5) + .5);
                }

                for (int x = 0; x <= gridSizeMm.x; x += 10)
                {
                    Vector2 start = new Vector2(x, 0) + gridOffset;
                    Vector2 end = new Vector2(x, gridSizeMm.y) + gridOffset;
                    transform.transform(ref start);
                    transform.transform(ref end);
                    grid.MoveTo((int)(start.x + .5) + .5, (int)(start.y + .5));
                    grid.LineTo((int)(end.x + .5) + .5, (int)(end.y + .5));
                }
            }
        }

        public override void OnMouseDown(MouseEventArgs mouseEvent)
        {
            base.OnMouseDown(mouseEvent);
            if (MouseCaptured)
            {
                mouseDownPosition.x = mouseEvent.X;
                mouseDownPosition.y = mouseEvent.Y;

                lastMousePosition = mouseDownPosition;
            }
        }

        public override void OnMouseWheel(MouseEventArgs mouseEvent)
        {
            base.OnMouseWheel(mouseEvent);
            if (FirstWidgetUnderMouse) // TODO: find a good way to decide if you are what the wheel is trying to do
            {
                Vector2 mousePreScale = new Vector2(mouseEvent.X, mouseEvent.Y);
                TotalTransform.inverse_transform(ref mousePreScale);

                const double deltaFor1Click = 120;
                layerScale = layerScale + layerScale * (mouseEvent.WheelDelta / deltaFor1Click) * .1;

                Vector2 mousePostScale = new Vector2(mouseEvent.X, mouseEvent.Y);
                TotalTransform.inverse_transform(ref mousePostScale);

                unscaledRenderOffset += (mousePostScale - mousePreScale);

                Invalidate();
            }
        }

        public override void OnMouseMove(MouseEventArgs mouseEvent)
        {
            base.OnMouseMove(mouseEvent);
            Vector2 mousePos = new Vector2(mouseEvent.X, mouseEvent.Y);
            if (MouseCaptured)
            {
                Vector2 mouseDelta = mousePos - lastMousePosition;
                switch (TransformState)
                {
                    case ETransformState.Move:
                        ScallingTransform.inverse_transform(ref mouseDelta);

                        unscaledRenderOffset += mouseDelta;
                        break;

                    case ETransformState.Scale:
                        double zoomDelta = 1;
                        if (mouseDelta.y < 0)
                        {
                            zoomDelta = 1 - (-1 * mouseDelta.y / 100);
                        }
                        else if(mouseDelta.y > 0)
                        {
                            zoomDelta = 1 + (1 * mouseDelta.y / 100);
                        }

                        Vector2 mousePreScale = mouseDownPosition;
                        TotalTransform.inverse_transform(ref mousePreScale);


                        layerScale *= zoomDelta;

                        Vector2 mousePostScale = mouseDownPosition;
                        TotalTransform.inverse_transform(ref mousePostScale);
                        
                        unscaledRenderOffset += (mousePostScale - mousePreScale);
                        break;

                    default:
                        throw new NotImplementedException();
                }

                Invalidate();
            }
            lastMousePosition = mousePos;
        }

        public void Load(string gcodePathAndFileName)
        {
            this.FileNameAndPath = gcodePathAndFileName;
            backgroundWorker = new BackgroundWorker();
            backgroundWorker.WorkerReportsProgress = true;
            backgroundWorker.WorkerSupportsCancellation = true;

            backgroundWorker.ProgressChanged += new ProgressChangedEventHandler(backgroundWorker_ProgressChanged);
            backgroundWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(backgroundWorker_RunWorkerCompleted);

            loadedGCode = null;
            GCodeFile.LoadInBackground(backgroundWorker, gcodePathAndFileName);
        }

        public override void OnClosed(EventArgs e)
        {
            if (backgroundWorker != null)
            {
                backgroundWorker.CancelAsync();
            }
            base.OnClosed(e);
        }

        public override RectangleDouble LocalBounds
        {
            get
            {
                return base.LocalBounds;
            }
            set
            {
                double oldWidth = Width;
                double oldHeight = Height;
                base.LocalBounds = value;
                if (oldWidth > 0)
                {
                    layerScale = layerScale * (Width / oldWidth);
                }
                else if(gCodeRenderer != null)
                {
                    CenterPartInView();
                }
            }
        }

        public void CenterPartInView()
        {
            gCodeRenderer = new GCodeRenderer(loadedGCode);
            RectangleDouble partBounds = loadedGCode.GetBounds();
            Vector2 weightedCenter = loadedGCode.GetWeightedCenter();

            unscaledRenderOffset = -weightedCenter;
            layerScale = Math.Min(Height / partBounds.Height, Width / partBounds.Width);

            Invalidate();
        }
    }
}