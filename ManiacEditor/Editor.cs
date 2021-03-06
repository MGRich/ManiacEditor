﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using ManiacEditor.Actions;
using ManiacEditor.Enums;
using RSDKv5;
using SharpDX.Direct3D9;
using Color = System.Drawing.Color;

namespace ManiacEditor
{
    public partial class Editor : Form, IDrawArea
    {
        bool dragged;
        bool startDragged;
        int lastX, lastY, draggedX, draggedY;
        int ShiftX = 0, ShiftY = 0, ScreenWidth, ScreenHeight;
        public bool showTileID;
        public bool showGrid;
        public bool showCollisionA;
        public bool showCollisionB;

        public List<Bitmap> CollisionLayerA = new List<Bitmap>();
        public List<Bitmap> CollisionLayerB = new List<Bitmap>();

        public List<string> ObjectList = new List<string>(); //All Gameconfig + Stageconfig Object names

        int ClickedX = -1, ClickedY = -1;
        string scrollDirection = "X";

        public Stack<IAction> undo = new Stack<IAction>();
        public Stack<IAction> redo = new Stack<IAction>();

        bool draggingSelection;
        int selectingX, selectingY;
        bool zooming;
        double Zoom = 1;
        int ZoomLevel = 0;
        public String ToolbarSelectedTile;
        bool SceneLoaded = false;
        bool AllowSceneChange = false;

        public static string DataDirectory;

        GameConfig GameConfig;

        public string SelectedZone;
        string SelectedScene;

        internal StageTiles StageTiles;
        internal EditorScene EditorScene;
        internal StageConfig StageConfig;

        string SceneFilename = null;
        string StageConfigFileName = null;

        internal EditorLayer FGHigher => EditorScene?.HighDetails;
        internal EditorLayer FGHigh => EditorScene?.ForegroundHigh;
        internal EditorLayer FGLow => EditorScene?.ForegroundLow;
        internal EditorLayer FGLower => EditorScene?.LowDetails;
        private IList<ToolStripButton> _extraLayerButtons;

        internal EditorBackground Background;

        internal EditorLayer EditLayer;


        internal TilesToolbar TilesToolbar = null;
        private EntitiesToolbar entitiesToolbar = null;

        internal Dictionary<Point, ushort> TilesClipboard;
        private List<EditorEntity> entitiesClipboard;
        public int SelectedTilesCount;
        public int DeselectTilesCount;
        internal int SelectedTileX;
        internal int SelectedTileY;
        internal bool controlWindowOpen;

        internal int SceneWidth => EditorScene.Layers.Max(sl => sl.Width) * 16;
        internal int SceneHeight => EditorScene.Layers.Max(sl => sl.Height) * 16;

        bool scrolling = false;
        bool scrollingDragged = false, wheelClicked = false;
        Point scrollPosition;

        EditorEntities entities;

        public static Editor Instance;

        private IList<ToolStripMenuItem> _recentDataItems;
        public static ProcessMemory GameMemory = new ProcessMemory();
        public static bool GameRunning = false;
        public static string GamePath = "";

        public SharpPresence.Discord.RichPresence RPCcontrol = new SharpPresence.Discord.RichPresence(); //For Discord RPC
        public SharpPresence.Discord.EventHandlers RPCEventHandler = new SharpPresence.Discord.EventHandlers(); //For Discord RPC
        public string ScenePath = ""; //For Discord RPC

        public Editor()
        {
            Instance = this;
            InitializeComponent();
            InitDiscord();

            this.splitContainer1.Panel2MinSize = 254;

            GraphicPanel.GotFocus += new EventHandler(OnGotFocus);
            GraphicPanel.LostFocus += new EventHandler(OnLostFocus);

            GraphicPanel.Width = SystemInformation.PrimaryMonitorSize.Width;
            GraphicPanel.Height = SystemInformation.PrimaryMonitorSize.Height;

            _extraLayerButtons = new List<ToolStripButton>();
            _recentDataItems = new List<ToolStripMenuItem>();

            SetViewSize();

            UpdateControls();

            TryLoadSettings();
        }

        public void InitDiscord()
        {
            SharpPresence.Discord.Initialize("484279851830870026", RPCEventHandler);

            if (Properties.Settings.Default.ShowDiscordRPC)
            {
                RPCcontrol.state = "Maniac Editor";
                RPCcontrol.details = "Idle";

                RPCcontrol.largeImageKey = "maniac";
                RPCcontrol.largeImageText = "maniac-small";

                TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
                int secondsSinceEpoch = (int)t.TotalSeconds;

                RPCcontrol.startTimestamp = secondsSinceEpoch;

                SharpPresence.Discord.RunCallbacks();
                SharpPresence.Discord.UpdatePresence(RPCcontrol);
            }
            else
            {
                RPCcontrol.state = "Maniac Editor";
                RPCcontrol.details = "";

                RPCcontrol.largeImageKey = "maniac";
                RPCcontrol.largeImageText = "Maniac Editor";

                TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
                int secondsSinceEpoch = (int)t.TotalSeconds;

                RPCcontrol.startTimestamp = secondsSinceEpoch;

                SharpPresence.Discord.RunCallbacks();
                SharpPresence.Discord.UpdatePresence(RPCcontrol);
            }
        }

        public void UpdateDiscord(string Details = null)
        {
            if (Properties.Settings.Default.ShowDiscordRPC)
            {
                SharpPresence.Discord.RunCallbacks();
                if (Details != null)
                {
                    RPCcontrol.details = Details;
                }
                else
                {
                    RPCcontrol.details = "Idle";
                }
                SharpPresence.Discord.UpdatePresence(RPCcontrol);
            }
            else
            {
                RPCcontrol.state = "Maniac Editor";
                RPCcontrol.details = "";

                RPCcontrol.largeImageKey = "maniac";
                RPCcontrol.largeImageText = "Maniac Editor";

                SharpPresence.Discord.RunCallbacks();
                SharpPresence.Discord.UpdatePresence(RPCcontrol);
            }
        }

        public void DisposeDiscord()
        {
            RPCcontrol.startTimestamp = 0;
            SharpPresence.Discord.Shutdown();
        }

        /// <summary>
        /// Try to load settings from the Application Settings file(s).
        /// This includes User specific settings.
        /// </summary>
        private void TryLoadSettings()
        {
            try
            {
                var mySettings = Properties.Settings.Default;
                if (mySettings.UpgradeRequired)
                {
                    mySettings.Upgrade();
                    mySettings.UpgradeRequired = false;
                    mySettings.Save();
                }
                
                WindowState = mySettings.IsMaximized ? FormWindowState.Maximized : WindowState;
                GamePath = mySettings.GamePath;

                if (mySettings.DataDirectories?.Count > 0)
                {
                    RefreshDataDirectories(mySettings.DataDirectories);
                }
            }
            catch (Exception ex)
            {
                Debug.Write("Failed to load settings: " + ex);
            }
        }

        /// <summary>
        /// Refreshes the Data directories displayed in the recent list under the File menu.
        /// </summary>
        /// <param name="settings">The settings file containing the </param>
        private void RefreshDataDirectories(StringCollection recentDataDirectories)
        {
            recentDataDirectoriesToolStripMenuItem.Visible = false;
            CleanUpRecentList();

            var startRecentItems = fileToolStripMenuItem.DropDownItems.IndexOf(recentDataDirectoriesToolStripMenuItem);

            foreach (var dataDirectory in recentDataDirectories)
            {
                _recentDataItems.Add(CreateDataDirectoryMenuLink(dataDirectory));
            }

            foreach (var menuItem in _recentDataItems.Reverse())
            {
                fileToolStripMenuItem.DropDownItems.Insert(startRecentItems,
                                                           menuItem);
            }
        }

        /// <summary>
        /// Removes any recent Data directories from the File menu.
        /// </summary>
        private void CleanUpRecentList()
        {
            foreach (var menuItem in _recentDataItems)
            {
                menuItem.Click -= RecentDataDirectoryClicked;
                fileToolStripMenuItem.DropDownItems.Remove(menuItem);
            }
            _recentDataItems.Clear();
        }

        private ToolStripMenuItem CreateDataDirectoryMenuLink(string target)
        {
            ToolStripMenuItem newItem = new ToolStripMenuItem(target)
            {
                Tag = target
            };
            newItem.Click += RecentDataDirectoryClicked;
            return newItem;
        }

        private void RecentDataDirectoryClicked(object sender, EventArgs e)
        {
            var menuItem = sender as ToolStripMenuItem;
            string dataDirectory = menuItem.Tag.ToString();
            var dataDirectories = Properties.Settings.Default.DataDirectories;
            Properties.Settings.Default.GamePath = GamePath;
            if (IsDataDirectoryValid(dataDirectory))
            {
                ResetDataDirectoryToAndResetScene(dataDirectory);
            }
            else
            {
                MessageBox.Show($"The specified Data Directory {dataDirectory} is not valid.",
                                "Invalid Data Directory!",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                dataDirectories.Remove(dataDirectory);
                RefreshDataDirectories(dataDirectories);
            }
            Properties.Settings.Default.Save();
        }

        private void ResetDataDirectoryToAndResetScene(string newDataDirectory)
        {
            UnloadScene();
            UseVisibilityPrefrences();
            DataDirectory = newDataDirectory;
            AddRecentDataFolder(newDataDirectory);
            SetGameConfig();
            if (Properties.Settings.Default.forceBrowse == true)
                OpenSceneManual();
            else
                OpenScene();

        }

        private bool IsEditing()
        {
            return IsTilesEdit() || IsEntitiesEdit();
        }

        private bool IsTilesEdit()
        {
            return EditLayer != null;
        }

        private bool IsEntitiesEdit()
        {
            return EditEntities.Checked;
        }


        private bool IsSelected()
        {
            if (IsTilesEdit())
            {
                return EditLayer.SelectedTiles.Count > 0 || EditLayer.TempSelectionTiles.Count > 0;
            }
            else if (IsEntitiesEdit())
            {
                return entities.IsSelected();
            }
            return false;
        }

        private void SetSceneOnlyButtonsState(bool enabled)
        {
            saveToolStripMenuItem.Enabled = enabled;
            saveAsToolStripMenuItem.Enabled = enabled;
            exportToolStripMenuItem.Enabled = enabled;

            ShowFGHigh.Enabled = enabled && FGHigh != null;
            ShowFGLow.Enabled = enabled && FGLow != null;
            ShowFGHigher.Enabled = enabled && FGHigher != null;
            ShowFGLower.Enabled = enabled && FGLower != null;
            ShowEntities.Enabled = enabled;
            ShowAnimations.Enabled = enabled;
            ReloadToolStripButton.Enabled = enabled;

            Save.Enabled = enabled;
            
            if (Properties.Settings.Default.ReduceZoom)
            {
                zoomInButton.Enabled = enabled && ZoomLevel < 5;
                zoomOutButton.Enabled = enabled && ZoomLevel > -2;
            }
            else
            {
                zoomInButton.Enabled = enabled && ZoomLevel < 5;
                zoomOutButton.Enabled = enabled && ZoomLevel > -5;
            }
            
            

            RunScene.Enabled = enabled && !GameRunning;

            SetEditButtonsState(enabled);
            UpdateTooltips();
        }

        private void SetSelectOnlyButtonsState(bool enabled = true)
        {
            enabled &= IsSelected();
            deleteToolStripMenuItem.Enabled = enabled;
            copyToolStripMenuItem.Enabled = enabled;
            cutToolStripMenuItem.Enabled = enabled;
            duplicateToolStripMenuItem.Enabled = enabled;

            flipHorizontalToolStripMenuItem.Enabled = enabled && IsTilesEdit();
            flipVerticalToolStripMenuItem.Enabled = enabled && IsTilesEdit();

            if (IsEntitiesEdit())
            {
                entitiesToolbar.SelectedEntities = entities.SelectedEntities.Select(x => x.Entity).ToList();
            }
        }

        private void SetEditButtonsState(bool enabled)
        {
            EditFGLow.Enabled = enabled && FGLow != null;
            EditFGHigh.Enabled = enabled && FGHigh != null;
            EditFGLower.Enabled = enabled && FGLower != null;
            EditFGHigher.Enabled = enabled && FGHigher != null;
            EditEntities.Enabled = enabled;
            importObjectsToolStripMenuItem.Enabled = enabled && StageConfig != null;
            removeObjectToolStripMenuItem.Enabled = enabled && StageConfig != null;
            importSoundsToolStripMenuItem.Enabled = enabled && StageConfig != null;
            layerManagerToolStripMenuItem.Enabled = enabled;

            if (enabled && EditFGLow.Checked) EditLayer = FGLow;
            else if (enabled && EditFGHigh.Checked) EditLayer = FGHigh;
            else if (enabled && EditFGHigher.Checked) EditLayer = FGHigher;
            else if (enabled && EditFGLower.Checked) EditLayer = FGLower;
            else if (enabled && _extraLayerButtons.Any(elb => elb.Checked))
            {
                var selectedExtraLayerButton = _extraLayerButtons.Single(elb => elb.Checked);
                var editorLayer = EditorScene.OtherLayers.Single(el => el.Name.Equals(selectedExtraLayerButton.Text));

                EditLayer = editorLayer;
            }
            else EditLayer = null;

            undoToolStripMenuItem.Enabled = enabled && undo.Count > 0;
            redoToolStripMenuItem.Enabled = enabled && redo.Count > 0;

            undoButton.Enabled = enabled && undo.Count > 0;
            redoButton.Enabled = enabled && redo.Count > 0;

            pointerButton.Enabled = enabled && IsTilesEdit();
            selectTool.Enabled = enabled && IsTilesEdit();
            placeTilesButton.Enabled = enabled && IsTilesEdit();

            showGridButton.Enabled = enabled && StageConfig != null;
            showCollisionAButton.Enabled = enabled && StageConfig != null;
            showCollisionBButton.Enabled = enabled && StageConfig != null;
            showTileIDButton.Enabled = enabled && StageConfig != null;

            if (enabled && IsTilesEdit() && (TilesClipboard != null || Clipboard.ContainsData("ManiacTiles")))
                pasteToolStripMenuItem.Enabled = true;
            else
                pasteToolStripMenuItem.Enabled = false;

            if (IsTilesEdit())
            {
                if (TilesToolbar == null)
                {
                    TilesToolbar = new TilesToolbar(StageTiles);
                    TilesToolbar.TileDoubleClick = new Action<int>(x =>
                    {
                        EditorPlaceTile(new Point((int)(ShiftX / Zoom) + EditorLayer.TILE_SIZE - 1, (int)(ShiftY / Zoom) + EditorLayer.TILE_SIZE - 1), x);
                    });
                    TilesToolbar.TileOptionChanged = new Action<int, bool>((option, state) =>
                   {
                       EditLayer.SetPropertySelected(option + 12, state);
                   });
                    splitContainer1.Panel2.Controls.Clear();
                    splitContainer1.Panel2.Controls.Add(TilesToolbar);
                    splitContainer1.Panel2Collapsed = false;
                    TilesToolbar.Width = splitContainer1.Panel2.Width - 2;
                    TilesToolbar.Height = splitContainer1.Panel2.Height - 2;
                    Form1_Resize(null, null);
                }
                UpdateTilesOptions();
                TilesToolbar.ShowShortcuts = placeTilesButton.Checked;
            }
            else
            {
                TilesToolbar?.Dispose();
                TilesToolbar = null;
            }
            if (IsEntitiesEdit())
            {
                if (entitiesToolbar == null)
                {
                    entitiesToolbar = new EntitiesToolbar(EditorScene.Objects);
                    //entitiesToolbar = new EntitiesToolbar(ObjectList);
                    entitiesToolbar.SelectedEntity = new Action<int>(x =>
                    {
                        entities.SelectSlot(x);
                        SetSelectOnlyButtonsState();
                    });
                    entitiesToolbar.AddAction = new Action<IAction>(x =>
                    {
                        undo.Push(x);
                        redo.Clear();
                        UpdateControls();
                    });
                    entitiesToolbar.Spawn = new Action<SceneObject>(x =>
                    {
                        entities.Add(x, new Position((short)(ShiftX / Zoom), (short)(ShiftY / Zoom)));
                        undo.Push(entities.LastAction);
                        redo.Clear();
                        UpdateControls();
                    });
                    splitContainer1.Panel2.Controls.Clear();
                    splitContainer1.Panel2.Controls.Add(entitiesToolbar);
                    splitContainer1.Panel2Collapsed = false;
                    entitiesToolbar.Width = splitContainer1.Panel2.Width - 2;
                    entitiesToolbar.Height = splitContainer1.Panel2.Height - 2;
                    Form1_Resize(null, null);
                }
                UpdateEntitiesToolbarList();
                //entitiesToolbar.SelectedEntities = entities.SelectedEntities.Select(x => x.Entity).ToList();
            }
            else
            {
                entitiesToolbar?.Dispose();
                entitiesToolbar = null;
            }
            if (TilesToolbar == null && entitiesToolbar == null)
            {
                splitContainer1.Panel2Collapsed = true;
                Form1_Resize(null, null);
            }

            SetSelectOnlyButtonsState(enabled);
        }

        private void UpdateEntitiesToolbarList()
        {
            entitiesToolbar.Entities = entities.Entities.Select(x => x.Entity).ToList();
        }

        private void UpdateTilesOptions()
        {
            if (IsTilesEdit())
            {
                List<ushort> values = EditLayer.GetSelectedValues();
                if (values.Count > 0)
                {
                    for (int i = 0; i < 4; ++i)
                    {
                        bool set = ((values[0] & (1 << (i + 12))) != 0);
                        bool unk = false;
                        foreach (ushort value in values)
                        {
                            if (set != ((value & (1 << (i + 12))) != 0))
                            {
                                unk = true;
                                break;
                            }
                        }
                        TilesToolbar.SetTileOptionState(i, unk ? TilesToolbar.TileOptionState.Indeterminate : set ? TilesToolbar.TileOptionState.Checked : TilesToolbar.TileOptionState.Unchcked);
                    }
                }
                else
                {
                    for (int i = 0; i < 4; ++i)
                        TilesToolbar.SetTileOptionState(i, TilesToolbar.TileOptionState.Disabled);
                }
            }
        }

        private void UpdateControls()
        {
            SetSceneOnlyButtonsState(EditorScene != null);
        }

        public void OnGotFocus(object sender, EventArgs e)
        {
        }
        public void OnLostFocus(object sender, EventArgs e)
        {
        }

        public void EditorPlaceTile(Point position, int tile)
        {
            Dictionary<Point, ushort> tiles = new Dictionary<Point, ushort>();
            tiles[new Point(0, 0)] = (ushort)tile;
            EditLayer?.PasteFromClipboard(position, tiles);
            UpdateEditLayerActions();
        }

        public void MagnetDisable()
        {
        }

        private void UpdateEditLayerActions()
        {
            if (EditLayer != null)
            {
                List<IAction> actions = EditLayer.Actions;
                if (actions.Count > 0) redo.Clear();
                while (actions.Count > 0)
                {
                    bool create_new = false;
                    if (undo.Count == 0 || !(undo.Peek() is ActionsGroup))
                    {
                        create_new = true;
                    }
                    else
                    {
                        create_new = (undo.Peek() as ActionsGroup).IsClosed;
                    }
                    if (create_new)
                    {
                        undo.Push(new ActionsGroup());
                    }
                    (undo.Peek() as ActionsGroup).AddAction(actions[0]);
                    actions.RemoveAt(0);
                }
                UpdateControls();
            }
        }

        public void DeleteSelected()
        {
            EditLayer?.DeleteSelected();
            UpdateEditLayerActions();

            if (IsEntitiesEdit())
            {
                entities.DeleteSelected();
                UpdateLastEntityAction();
            }
        }

        public void UpdateLastEntityAction()
        {
            if (entities.LastAction != null)
            {
                redo.Clear();
                undo.Push(entities.LastAction);
                entities.LastAction = null;
                UpdateControls();
            }

        }

        public void GraphicPanel_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.ControlKey)
            {
                if (IsTilesEdit() && placeTilesButton.Checked)
                    TilesToolbar.SetSelectTileOption(0, true);
            }
            else if (e.KeyCode == Keys.ShiftKey)
            {
                if (IsTilesEdit() && placeTilesButton.Checked)
                    TilesToolbar.SetSelectTileOption(1, true);
            }
            else if (e.Control && e.KeyCode == Keys.O)
            {
                if (e.Alt)
                {
                    openDataDirectoryToolStripMenuItem_Click(null, null);
                }
                else
	            {
                    Open_Click(null, null); 
                }
            }
            else if (e.Control && e.KeyCode == Keys.N)
            {
                New_Click(null, null);
            }
            else if (e.Control && e.KeyCode == Keys.S)
            {
                if (e.Alt)
                {
                    saveAsToolStripMenuItem_Click(null, null);
                }
                else
                {
                    Save_Click(null, null);
                }
            }
            else if (e.Control && (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0))
            {
                SetZoomLevel(0, new Point(0, 0));
            }
            else if (e.KeyCode == Keys.Z)
            {
                EditorUndo();
            }
            else if (e.KeyCode == Keys.Y)
            {
                EditorRedo();
            }
            if (IsEditing())
            {
                if (e.Control)
                {
                    if (e.KeyCode == Keys.V)
                    {
                        pasteToolStripMenuItem_Click(sender, e);
                    }
                }
                if (IsSelected())
                {
                    if (e.KeyData == Keys.Delete)
                    {
                        DeleteSelected();
                    }
                    else if (e.KeyData == Keys.Up || e.KeyData == Keys.Down || e.KeyData == Keys.Left || e.KeyData == Keys.Right)
                    {
                        int x = 0, y = 0;
                        if (Properties.Settings.Default.EnableFasterNudge == false)
                        {
                            switch (e.KeyData)
                            {
                                case Keys.Up: y = -1; break;
                                case Keys.Down: y = 1; break;
                                case Keys.Left: x = -1; break;
                                case Keys.Right: x = 1; break;
                            }
                        }
                        else
                        {
                            switch (e.KeyData)
                            {
                                case Keys.Up: y = -1 - Properties.Settings.Default.FasterNudgeValue; break;
                                case Keys.Down: y = 1 + Properties.Settings.Default.FasterNudgeValue; break;
                                case Keys.Left: x = -1 - Properties.Settings.Default.FasterNudgeValue; break;
                                case Keys.Right: x = 1 + Properties.Settings.Default.FasterNudgeValue; break;
                            }
                        }
                        EditLayer?.MoveSelectedQuonta(new Point(x, y));
                        UpdateEditLayerActions();

                        if (IsEntitiesEdit())
                        {
                            entities.MoveSelected(new Point(0, 0), new Point(x, y), false);
                            entitiesToolbar.UpdateCurrentEntityProperites();

                            // Try to merge with last move
                            if (undo.Count > 0 && undo.Peek() is ActionMoveEntities && (undo.Peek() as ActionMoveEntities).UpdateFromKey(entities.SelectedEntities, new Point(x, y))) { }
                            else
                            {
                                undo.Push(new ActionMoveEntities(entities.SelectedEntities.ToList(), new Point(x, y), true));
                                redo.Clear();
                                UpdateControls();
                            }
                        }
                    }
                    else if (e.KeyData == Keys.F)
                    {
                        if (IsTilesEdit())
                            flipVerticalToolStripMenuItem_Click(sender, e);
                    }
                    else if (e.KeyData == Keys.M)
                    {
                        if (IsTilesEdit())
                            flipHorizontalToolStripMenuItem_Click(sender, e);
                    }
                    if (e.Control)
                    {
                        if (e.KeyCode == Keys.X)
                        {
                            cutToolStripMenuItem_Click(sender, e);
                        }
                        else if (e.KeyCode == Keys.C)
                        {
                            copyToolStripMenuItem_Click(sender, e);
                        }
                        else if (e.KeyCode == Keys.D)
                        {
                            duplicateToolStripMenuItem_Click(sender, e);
                        }
                    }
                }
            }
        }

        public void GraphicPanel_OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.ControlKey)
            {
                if (IsTilesEdit() && placeTilesButton.Checked)
                    TilesToolbar.SetSelectTileOption(0, false);
            }
            else if (e.KeyCode == Keys.ShiftKey)
            {   
                if (IsTilesEdit() && placeTilesButton.Checked)
                    TilesToolbar.SetSelectTileOption(1, false);
            }
            else if (e.KeyCode == Keys.N)
            {
                nudgeFasterButton_Click(sender,e);
            }
            else if (e.KeyCode == Keys.B)
            {
                scrollLockButton_Click(sender, e);
            }
        }

        private bool CtrlPressed()
        {
            return ModifierKeys.HasFlag(Keys.Control);
        }

        private bool ShiftPressed()
        {
            return ModifierKeys.HasFlag(Keys.Shift);
        }

        private void GraphicPanel_OnMouseDoubleClick(object sender, MouseEventArgs e)
        {
        }

        private void GraphicPanel_OnMouseMove(object sender, MouseEventArgs e)
        {
            if (ClickedX != -1)
            {
                Point clicked_point = new Point((int)(ClickedX / Zoom), (int)(ClickedY / Zoom));
                // There was just a click now we can determine that this click is dragging
                if (IsTilesEdit())
                {
                    if (EditLayer.IsPointSelected(clicked_point))
                    {
                        // Start dragging the tiles
                        dragged = true;
                        startDragged = true;
                        EditLayer.StartDrag();
                    }
                    else if (!selectTool.Checked && !ShiftPressed() && !CtrlPressed() && EditLayer.HasTileAt(clicked_point))
                    {
                        // Start dragging the single selected tile
                        EditLayer.Select(clicked_point);
                        dragged = true;
                        startDragged = true;
                        EditLayer.StartDrag();
                    }
                    else
                    {
                        // Start drag selection
                        //EditLayer.Select(clicked_point, ShiftPressed || CtrlPressed, CtrlPressed);
                        if (!ShiftPressed() && !CtrlPressed())
                            Deselect();
                        UpdateControls();
                        UpdateEditLayerActions();
                        draggingSelection = true;
                        selectingX = ClickedX;
                        selectingY = ClickedY;
                    }
                }
                else if (IsEntitiesEdit())
                {
                    if (entities.GetEntityAt(clicked_point)?.Selected ?? false)
                    {
                        ClickedX = e.X;
                        ClickedY = e.Y;
                        // Start dragging the entity
                        dragged = true;
                        draggedX = 0;
                        draggedY = 0;
                        startDragged = true;
                    }
                    else
                    {
                        // Start drag selection
                        if (!ShiftPressed() && !CtrlPressed())
                            Deselect();
                        UpdateControls();
                        draggingSelection = true;
                        selectingX = ClickedX;
                        selectingY = ClickedY;
                    }
                }
                ClickedX = -1;
                ClickedY = -1;
            }
            if (scrolling)
            {
                if (wheelClicked)
                {
                    scrollingDragged = true;
                }

                int xMove = (hScrollBar1.Visible) ? e.X - ShiftX - scrollPosition.X : 0;
                int yMove = (vScrollBar1.Visible) ? e.Y - ShiftY - scrollPosition.Y : 0;

                if (Math.Abs(xMove) < 15) xMove = 0;
                if (Math.Abs(yMove) < 15) yMove = 0;

                if (xMove > 0)
                {
                    if (yMove > 0) Cursor = Cursors.PanSE;
                    else if (yMove < 0) Cursor = Cursors.PanNE;
                    else Cursor = Cursors.PanEast;
                }
                else if (xMove < 0)
                {
                    if (yMove > 0) Cursor = Cursors.PanSW;
                    else if (yMove < 0) Cursor = Cursors.PanNW;
                    else Cursor = Cursors.PanWest;
                }
                else
                {
                    if (yMove > 0) Cursor = Cursors.PanSouth;
                    else if (yMove < 0) Cursor = Cursors.PanNorth;
                    else
                    {
                        if (vScrollBar1.Visible && hScrollBar1.Visible) Cursor = Cursors.NoMove2D;
                        else if (vScrollBar1.Visible) Cursor = Cursors.NoMoveVert;
                        else if (hScrollBar1.Visible) Cursor = Cursors.NoMoveHoriz;
                    }
                }

                Point position = new Point(ShiftX, ShiftY); ;
                int x = xMove / 10 + position.X;
                int y = yMove / 10 + position.Y;

                if (x < 0) x = 0;
                if (y < 0) y = 0;
                if (x > hScrollBar1.Maximum - hScrollBar1.LargeChange) x = hScrollBar1.Maximum - hScrollBar1.LargeChange;
                if (y > vScrollBar1.Maximum - vScrollBar1.LargeChange) y = vScrollBar1.Maximum - vScrollBar1.LargeChange;

                if (x != position.X || y != position.Y)
                {
                    if (vScrollBar1.Visible)
                    {
                        vScrollBar1.Value = y;
                    }
                    if (hScrollBar1.Visible)
                    {
                        hScrollBar1.Value = x;
                    }
                    GraphicPanel.Render();
                    GraphicPanel.OnMouseMoveEventCreate();
                }
            }

            //
            // Tooltip Bar Info 
            //

            positionLabel.Text = "X: " + (int)(e.X / Zoom) + " Y: " + (int)(e.Y / Zoom);

            if (Properties.Settings.Default.pixelCountMode == false)
            {
                selectedPositionLabel.Text = "Selected Tile Position: X: " + (int)SelectedTileX + ", Y: " + (int)SelectedTileY;
                selectedPositionLabel.ToolTipText = "The Position of the Selected Tile";
            }
            else
            {
                selectedPositionLabel.Text = "Selected Tile Pixel Position: " + "X: " + (int)SelectedTileX * 16 + ", Y: " + (int)SelectedTileY * 16;
                selectedPositionLabel.ToolTipText = "The Pixel Position of the Selected Tile";
            }
            if (Properties.Settings.Default.pixelCountMode == false)
            { 
                selectionSizeLabel.Text = "Amount of Tiles in Selection: " + (SelectedTilesCount - DeselectTilesCount);
                selectionSizeLabel.ToolTipText = "The Size of the Selection";
            }
            else
            {
                selectionSizeLabel.Text = "Length of Pixels in Selection: " + (SelectedTilesCount - DeselectTilesCount) * 16;
                selectionSizeLabel.ToolTipText = "The Length of all the Tiles (by Pixels) in the Selection";
            }

            //
            // End of Tooltip Bar Info Section
            //

            if (IsEditing())
            {
                if (IsTilesEdit() && placeTilesButton.Checked)
                {
                    Point p = new Point((int)(e.X / Zoom), (int)(e.Y / Zoom));
                    if (e.Button == MouseButtons.Left)
                    {
                        // Place tile
                        if (TilesToolbar.SelectedTile != -1)
                        {
                            if (EditLayer.GetTileAt(p) != TilesToolbar.SelectedTile)
                            {
                                EditorPlaceTile(p, TilesToolbar.SelectedTile);
                            }
                            else if (!EditLayer.IsPointSelected(p))
                            {
                                EditLayer.Select(p);
                            }
                        }
                    }
                    else if (e.Button == MouseButtons.Right)
                    {
                        // Remove tile
                        if (!EditLayer.IsPointSelected(p))
                        {
                            EditLayer.Select(p);
                        }
                        DeleteSelected();
                    }
                }
                if (draggingSelection || dragged)
                {
                    Point position = new Point(ShiftX, ShiftY); ;
                    int ScreenMaxX = position.X + splitContainer1.Panel1.Width - System.Windows.Forms.SystemInformation.VerticalScrollBarWidth;
                    int ScreenMaxY = position.Y + splitContainer1.Panel1.Height - System.Windows.Forms.SystemInformation.HorizontalScrollBarHeight;
                    int ScreenMinX = position.X;
                    int ScreenMinY = position.Y;

                    int x = position.X;
                    int y = position.Y;

                    if (e.X > ScreenMaxX)
                    {
                        x += (e.X - ScreenMaxX) / 10;
                    }
                    else if (e.X < ScreenMinX)
                    {
                        x += (e.X - ScreenMinX) / 10;
                    }
                    if (e.Y > ScreenMaxY)
                    {
                        y += (e.Y - ScreenMaxY) / 10;
                    }
                    else if (e.Y < ScreenMinY)
                    {
                        y += (e.Y - ScreenMinY) / 10;
                    }

                    if (x < 0) x = 0;
                    if (y < 0) y = 0;
                    if (x > hScrollBar1.Maximum - hScrollBar1.LargeChange) x = hScrollBar1.Maximum - hScrollBar1.LargeChange;
                    if (y > vScrollBar1.Maximum - vScrollBar1.LargeChange) y = vScrollBar1.Maximum - vScrollBar1.LargeChange;

                    if (x != position.X || y != position.Y)
                    {
                        if (vScrollBar1.Visible)
                        {
                            vScrollBar1.Value = y;
                        }
                        if (hScrollBar1.Visible)
                        {
                            hScrollBar1.Value = x;
                        }
                        GraphicPanel.Render();
                        GraphicPanel.OnMouseMoveEventCreate();
                    }

                }

                if (draggingSelection)
                {
                    if (selectingX != e.X && selectingY != e.Y)
                    {
                        int x1 = (int)(selectingX / Zoom), x2 = (int)(e.X / Zoom);
                        int y1 = (int)(selectingY / Zoom), y2 = (int)(e.Y / Zoom);
                        if (x1 > x2)
                        {
                            x1 = (int)(e.X / Zoom);
                            x2 = (int)(selectingX / Zoom);
                        }
                        if (y1 > y2)
                        {
                            y1 = (int)(e.Y / Zoom);
                            y2 = (int)(selectingY / Zoom);
                        }
                        EditLayer?.TempSelection(new Rectangle(x1, y1, x2 - x1, y2 - y1), CtrlPressed());
                        UpdateTilesOptions();

                        if (IsEntitiesEdit()) entities.TempSelection(new Rectangle(x1, y1, x2 - x1, y2 - y1), CtrlPressed());
                    }
                }
                else if (dragged)
                {
                    Point oldPoint = new Point((int)(lastX / Zoom), (int)(lastY / Zoom));
                    Point newPoint = new Point((int)(e.X / Zoom), (int)(e.Y / Zoom));

                    EditLayer?.MoveSelected(oldPoint, newPoint, CtrlPressed());
                    UpdateEditLayerActions();
                    if (IsEntitiesEdit())
                    {
                        try
                        {
                            entities.MoveSelected(oldPoint, newPoint, CtrlPressed() && startDragged);
                        }
                        catch (EditorEntities.TooManyEntitiesException)
                        {
                            MessageBox.Show("Too many entities! (limit: 2048)");
                            dragged = false;
                            return;
                        }
                        draggedX += newPoint.X - oldPoint.X;
                        draggedY += newPoint.Y - oldPoint.Y;
                        if (CtrlPressed() && startDragged)
                        {
                            UpdateEntitiesToolbarList();
                            SetSelectOnlyButtonsState();
                        }
                        entitiesToolbar.UpdateCurrentEntityProperites();
                    }
                    startDragged = false;
                }
            }
            lastX = e.X;
            lastY = e.Y;
        }
        private void GraphicPanel_OnMouseDown(object sender, MouseEventArgs e)
        {
            //GraphicPanel.Focus();
            if (e.Button == MouseButtons.Left)
            {
                if (IsEditing() && !dragged)
                {
                    if (IsTilesEdit())
                    {
                        if (placeTilesButton.Checked)
                        {
                            // Place tile
                            if (TilesToolbar.SelectedTile != -1)
                            {
                                EditorPlaceTile(new Point((int)(e.X / Zoom), (int)(e.Y / Zoom)), TilesToolbar.SelectedTile);
                            }
                        }
                        else
                        {
                            ClickedX = e.X;
                            ClickedY = e.Y;
                        }
                    }
                    else if (IsEntitiesEdit())
                    {
                        Point clicked_point = new Point((int)(e.X / Zoom), (int)(e.Y / Zoom));
                        if (entities.GetEntityAt(clicked_point)?.Selected ?? false)
                        {
                            // We will have to check if this dragging or clicking
                            ClickedX = e.X;
                            ClickedY = e.Y;
                        }
                        else if (!ShiftPressed() && !CtrlPressed() && entities.GetEntityAt(clicked_point) != null)
                        {
                            entities.Select(clicked_point);
                            SetSelectOnlyButtonsState();
                            // Start dragging the single selected entity
                            dragged = true;
                            draggedX = 0;
                            draggedY = 0;
                            startDragged = true;
                        }
                        else
                        {
                            ClickedX = e.X;
                            ClickedY = e.Y;
                        }
                    }
                }

                if (scrolling)
                {
                    scrolling = false;
                    Cursor = Cursors.Default;
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (IsTilesEdit() && placeTilesButton.Checked)
                {
                    // Remove tile
                    Point p = new Point((int)(e.X / Zoom), (int)(e.Y / Zoom));
                    if (!EditLayer.IsPointSelected(p))
                    {
                        EditLayer.Select(p);
                    }
                    DeleteSelected();
                }
            }
            else if (e.Button == MouseButtons.Middle)
            {
                wheelClicked = true;
                scrolling = true;
                scrollingDragged = false;
                scrollPosition = new Point(e.X - ShiftX, e.Y - ShiftY);
                if (vScrollBar1.Visible && hScrollBar1.Visible)
                {
                    Cursor = Cursors.NoMove2D;
                }
                else if (vScrollBar1.Visible)
                {
                    Cursor = Cursors.NoMoveVert;
                }
                else if (hScrollBar1.Visible)
                {
                    Cursor = Cursors.NoMoveHoriz;
                }
                else
                {
                    scrolling = false;
                }
            }
        }
        private void GraphicPanel_OnMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (IsEditing())
                {
                    MagnetDisable();
                    if (draggingSelection)
                    {
                        if (selectingX != e.X && selectingY != e.Y)
                        {

                            int x1 = (int)(selectingX / Zoom), x2 = (int)(e.X / Zoom);
                            int y1 = (int)(selectingY / Zoom), y2 = (int)(e.Y / Zoom);
                            if (x1 > x2)
                            {
                                x1 = (int)(e.X / Zoom);
                                x2 = (int)(selectingX / Zoom);
                            }
                            if (y1 > y2)
                            {
                                y1 = (int)(e.Y / Zoom);
                                y2 = (int)(selectingY / Zoom);
                            }

                            EditLayer?.Select(new Rectangle(x1, y1, x2 - x1, y2 - y1), ShiftPressed() || CtrlPressed(), CtrlPressed());
                            if (IsEntitiesEdit()) entities.Select(new Rectangle(x1, y1, x2 - x1, y2 - y1), ShiftPressed() || CtrlPressed(), CtrlPressed());
                            SetSelectOnlyButtonsState();
                            UpdateEditLayerActions();
                        }
                        draggingSelection = false;
                        EditLayer?.EndTempSelection();
                        if (IsEntitiesEdit()) entities.EndTempSelection();
                    }
                    else
                    {
                        if (ClickedX != -1)
                        {
                            // So it was just click
                            Point clicked_point = new Point((int)(ClickedX / Zoom), (int)(ClickedY / Zoom));
                            if (IsTilesEdit())
                            {
                                EditLayer.Select(clicked_point, ShiftPressed() || CtrlPressed(), CtrlPressed());
                                UpdateEditLayerActions();
                            }
                            else if (IsEntitiesEdit())
                            {
                                entities.Select(clicked_point, ShiftPressed() || CtrlPressed(), CtrlPressed());
                            }
                            SetSelectOnlyButtonsState();
                            ClickedX = -1;
                            ClickedY = -1;
                        }
                        if (dragged && (draggedX != 0 || draggedY != 0))
                        {
                            if (IsEntitiesEdit())
                            {
                                IAction action = new ActionMoveEntities(entities.SelectedEntities.ToList(), new Point(draggedX, draggedY));
                                if (entities.LastAction != null)
                                {
                                    // If it is move & duplicate, merge them together
                                    var taction = new ActionsGroup();
                                    taction.AddAction(entities.LastAction);
                                    entities.LastAction = null;
                                    taction.AddAction(action);
                                    taction.Close();
                                    action = taction;
                                }
                                undo.Push(action);
                                redo.Clear();
                                UpdateControls();
                            }
                        }
                        dragged = false;
                    }
                }
            }
            else if (e.Button == MouseButtons.Middle)
            {
                wheelClicked = false;
                if (scrollingDragged)
                {
                    scrolling = false;
                    Cursor = Cursors.Default;
                }
            }
        }

        private void GraphicPanel_MouseWheel(object sender, MouseEventArgs e)
        {
            //GraphicPanel.Focus();
            if (CtrlPressed())
            {
                int maxZoom;
                int minZoom;
                if (Properties.Settings.Default.ReduceZoom)
                {
                    maxZoom = 5;
                    minZoom = -2;
                }
                else
                {
                    maxZoom = 5;
                    minZoom = -5;
                }
                int change = e.Delta / 120;
                ZoomLevel += change;
                if (ZoomLevel > maxZoom) ZoomLevel = maxZoom;
                if (ZoomLevel < minZoom) ZoomLevel = minZoom;

                SetZoomLevel(ZoomLevel, new Point(e.X - ShiftX, e.Y - ShiftY));
            }
            else
            {
                if (vScrollBar1.Visible || hScrollBar1.Visible)
                {
                    if (scrollDirection == "Y" && !Properties.Settings.Default.scrollLock)
                    {
                        if (vScrollBar1.Visible)
                        {
                            int y = vScrollBar1.Value - e.Delta;
                            if (y < 0) y = 0;
                            if (y > vScrollBar1.Maximum - vScrollBar1.LargeChange) y = vScrollBar1.Maximum - vScrollBar1.LargeChange;
                            vScrollBar1.Value = y;
                        }
                        else
                        {
                            int x = hScrollBar1.Value - e.Delta * 2;
                            if (x < 0) x = 0;
                            if (x > hScrollBar1.Maximum - hScrollBar1.LargeChange) x = hScrollBar1.Maximum - hScrollBar1.LargeChange;
                            hScrollBar1.Value = x;
                        }
                    }
                    else if (scrollDirection == "X" && !Properties.Settings.Default.scrollLock)
                    {
                        if (hScrollBar1.Visible)
                        {
                            int x = hScrollBar1.Value - e.Delta * 2;
                            if (x < 0) x = 0;
                            if (x > hScrollBar1.Maximum - hScrollBar1.LargeChange) x = hScrollBar1.Maximum - hScrollBar1.LargeChange;
                            hScrollBar1.Value = x;
                        }
                        else
                        {
                            int y = vScrollBar1.Value - e.Delta;
                            if (y < 0) y = 0;
                            if (y > vScrollBar1.Maximum - vScrollBar1.LargeChange) y = vScrollBar1.Maximum - vScrollBar1.LargeChange;
                            vScrollBar1.Value = y;
                        }
                    }
                    else if (scrollDirection == "Locked" || Properties.Settings.Default.scrollLock == true)
                    {
                        if (Properties.Settings.Default.ScrollLockDirection == false)
                        {
                            if (vScrollBar1.Visible)
                            {
                                int y = vScrollBar1.Value - e.Delta * 2;
                                if (y < 0) y = 0;
                                if (y > vScrollBar1.Maximum - vScrollBar1.LargeChange) y = vScrollBar1.Maximum - vScrollBar1.LargeChange;
                                vScrollBar1.Value = y;
                            }
                            else
                            {
                                int x = vScrollBar1.Value - e.Delta * 2;
                                if (x < 0) x = 0;
                                if (x > vScrollBar1.Maximum - vScrollBar1.LargeChange) x = vScrollBar1.Maximum - vScrollBar1.LargeChange;
                                vScrollBar1.Value = x;
                            }
                        }
                        else
                        {
                            if (hScrollBar1.Visible)
                            {
                                int x = hScrollBar1.Value - e.Delta * 2;
                                if (x < 0) x = 0;
                                if (x > hScrollBar1.Maximum - hScrollBar1.LargeChange) x = hScrollBar1.Maximum - hScrollBar1.LargeChange;
                                hScrollBar1.Value = x;
                            }
                            else
                            {
                                int y = vScrollBar1.Value - e.Delta;
                                if (y < 0) y = 0;
                                if (y > vScrollBar1.Maximum - vScrollBar1.LargeChange) y = vScrollBar1.Maximum - vScrollBar1.LargeChange;
                                vScrollBar1.Value = y;
                            }
                        }

                    }
                }

            }
        }

        public void SetZoomLevel(int zoom_level, Point zoom_point)
        {
            double old_zoom = Zoom;

            ZoomLevel = zoom_level;

            switch (ZoomLevel)
            {
                case 5: Zoom = 4; break;
                case 4: Zoom = 3; break;
                case 3: Zoom = 2; break;
                case 2: Zoom = 3 / 2.0; break;
                case 1: Zoom = 5 / 4.0; break;
                case 0: Zoom = 1; break;
                case -1: Zoom = 2 / 3.0; break;
                case -2: Zoom = 1 / 2.0; break;
                case -3: Zoom = 1 / 3.0; break;
                case -4: Zoom = 1 / 4.0; break;
                case -5: Zoom = 1 / 8.0; break;
            }

            zooming = true;

            int oldShiftX = ShiftX;
            int oldShiftY = ShiftY;

            if (EditorScene != null)
                SetViewSize((int)(SceneWidth * Zoom), (int)(SceneHeight * Zoom));

            if (hScrollBar1.Visible)
            {
                ShiftX = (int)((zoom_point.X + oldShiftX) / old_zoom * Zoom - zoom_point.X);
                ShiftX = Math.Min(hScrollBar1.Maximum - hScrollBar1.LargeChange, Math.Max(0, ShiftX));
                hScrollBar1.Value = ShiftX;
            }
            if (vScrollBar1.Visible)
            {
                ShiftY = (int)((zoom_point.Y + oldShiftY) / old_zoom * Zoom - zoom_point.Y);
                ShiftY = Math.Min(vScrollBar1.Maximum - vScrollBar1.LargeChange, Math.Max(0, ShiftY));
                vScrollBar1.Value = ShiftY;
            }

            zooming = false;

            UpdateControls();
        }

        private bool load()
        {
            if (DataDirectory == null)
            {
                do
                {
                    MessageBox.Show("Please select the \"Data\" folder", "Message");
                    string newDataDirectory = GetDataDirectory();

                    // allow user to quit gracefully
                    if (string.IsNullOrWhiteSpace(newDataDirectory)) return false;
                    if (IsDataDirectoryValid(newDataDirectory))
                    {
                        DataDirectory = newDataDirectory;
                    }
                }
                while (null == DataDirectory);

                SetGameConfig();
                AddRecentDataFolder(DataDirectory);
            }
            // Clears all the Textures
            EditorEntity.ReleaseResources();
            return true;
        }

        private string GetDataDirectory()
        {
            using (var folderBrowserDialog = new FolderSelectDialog())
            {
                folderBrowserDialog.Title = "Select Data Folder";

                if (!folderBrowserDialog.ShowDialog())
                    return null;

                return folderBrowserDialog.FileName;
            }
        }

        private bool IsDataDirectoryValid()
        {
            return IsDataDirectoryValid(DataDirectory);
        }

        private bool IsDataDirectoryValid(string directoryToCheck)
        {
            return File.Exists(Path.Combine(directoryToCheck, "Game", "GameConfig.bin"));
        }

        /// <summary>
        /// Sets the GameConfig property in relation to the DataDirectory property.
        /// </summary>
        private void SetGameConfig()
        {
            GameConfig = new GameConfig(Path.Combine(DataDirectory, "Game", "GameConfig.bin"));
        }

        /// <summary>
        /// Adds a Data directory to the persisted list, and refreshes the UI.
        /// </summary>
        /// <param name="dataDirectory">Path to the Data directory</param>
        private void AddRecentDataFolder(string dataDirectory)
        {
            try
            {
                var mySettings = Properties.Settings.Default;
                var dataDirectories = mySettings.DataDirectories;

                if (dataDirectories == null)
                {
                    dataDirectories = new StringCollection();
                    mySettings.DataDirectories = dataDirectories;
                }

                if (dataDirectories.Contains(dataDirectory))
                {
                    dataDirectories.Remove(dataDirectory);
                }

                if (dataDirectories.Count >= 10)
                {
                    for (int i = 9; i < dataDirectories.Count; i++)
                    {
                        dataDirectories.RemoveAt(i);
                    }
                }

                dataDirectories.Insert(0, dataDirectory);

                mySettings.Save();

                RefreshDataDirectories(dataDirectories);

                _baseDataDirectoryLabel.Text = string.Format(_baseDataDirectoryLabel.Tag.ToString(), 
                                                             dataDirectory);
            }
            catch (Exception ex)
            {
                Debug.Write("Failed to add data folder to recent list: " + ex);
            }
        }

        void UnloadScene()
        {
            SceneLoaded = false;
            EditorScene?.Dispose();
            EditorScene = null;
            SceneFilename = null;
            StageConfig = null;
            StageConfigFileName = null;

            SelectedScene = null;
            SelectedZone = null;

            if (StageTiles != null) StageTiles.Dispose();
            StageTiles = null;

            TearDownExtraLayerButtons();

            Background = null;

            if (!Properties.Settings.Default.ForceCopyUnlock)
            {
                TilesClipboard = null;
                entitiesClipboard = null;
            }
            if (Properties.Settings.Default.ProhibitEntityUseOnExternalClipboard)
            {
                entitiesClipboard = null;
            }


            entities = null;

            Zoom = 1;
            ZoomLevel = 0;

            undo.Clear();
            redo.Clear();

            EditFGLow.Checked = false;
            EditFGHigh.Checked = false;
            EditFGLower.Checked = false;
            EditFGHigher.Checked = false;
            EditEntities.Checked = false;

            SetViewSize();

            UpdateControls();

            // clear memory a little more aggressively 
            EditorEntity.ReleaseResources();
            GC.Collect();
        }

        void UseVisibilityPrefrences()
        {
            if (!Properties.Settings.Default.FGLowerDefault)
            {
                ShowFGLower.Checked = false;
            }
            else
            {
                ShowFGLower.Checked = true;
            }
            if (!Properties.Settings.Default.FGLowDefault)
            {
                ShowFGLow.Checked = false;
            }
            else
            {
                ShowFGLow.Checked = true;
            }
            if (!Properties.Settings.Default.FGHighDefault)
            {
                ShowFGHigh.Checked = false;
            }
            else
            {
                ShowFGHigh.Checked = true;
            }
            if (!Properties.Settings.Default.FGHigherDefault)
            {
                ShowFGHigher.Checked = false;
            }
            else
            {
                ShowFGHigher.Checked = true;
            }
            if (!Properties.Settings.Default.EntitiesDefault)
            {
                ShowEntities.Checked = false;
            }
            else
            {
                ShowEntities.Checked = true;
            }
            if (!Properties.Settings.Default.AnimationsDefault)
            {
                ShowAnimations.Checked = false;
            }
            else
            {
                ShowAnimations.Checked = true;
            }
        }

        private void Open_Click(object sender, EventArgs e)
        {
            Editor.Instance.SceneChangeWarning(sender, e);
            if (AllowSceneChange == true || SceneLoaded == false || Properties.Settings.Default.DisableSaveWarnings == true)
            {
                AllowSceneChange = false;
                if (!load()) return;
                if (Properties.Settings.Default.forceBrowse == true)
                    OpenSceneManual();
                else
                    OpenScene();

            }
            else
            {
                return;
            }

        }

        private void OpenScene()
        {

                SceneSelect select = new SceneSelect(GameConfig);
                select.ShowDialog();

                if (select.Result == null)
                    return;

                UnloadScene();
                UseVisibilityPrefrences();

                try
                {
                    if (File.Exists(select.Result))
                    {
                        // Selected file
                        // Don't forget to populate these Members
                        string directoryPath = Path.GetDirectoryName(select.Result);
                        SelectedZone = new DirectoryInfo(directoryPath).Name;
                        SelectedScene = Path.GetFileName(select.Result);

                        StageTiles = new StageTiles(directoryPath);

                        SceneFilename = select.Result;
                    }
                    else
                    {                    
                        SelectedZone = select.Result.Replace(Path.GetFileName(select.Result),"");
                        SelectedScene = Path.GetFileName(select.Result);

                        StageTiles = new StageTiles(Path.Combine(DataDirectory, "Stages", SelectedZone));
                        SceneFilename = Path.Combine(DataDirectory, "Stages", SelectedZone, SelectedScene);
                    }

                //These cause issues, but not clearing them means when new stages are loaded Collision Mask 0 will be index 1024... (I think)
                CollisionLayerA.Clear();
                CollisionLayerB.Clear();

                for (int i = 0; i < 1024; i++)
                {
                    CollisionLayerA.Add(StageTiles.Config.CollisionPath1[i].DrawCMask(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(255, 255, 255, 255)));
                    CollisionLayerB.Add(StageTiles.Config.CollisionPath2[i].DrawCMask(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(255, 255, 255, 255)));
                }

                EditorScene = new EditorScene(SceneFilename);
                    StageConfigFileName = Path.Combine(Path.GetDirectoryName(SceneFilename), "StageConfig.bin");
                    if (File.Exists(StageConfigFileName))
                    {
                        StageConfig = new StageConfig(StageConfigFileName);
                    }

                ObjectList.Clear();
                for (int i = 0; i < GameConfig.ObjectsNames.Count; i++)
                {
                    ObjectList.Add(GameConfig.ObjectsNames[i]);
                }
                for (int i = 0; i < StageConfig.ObjectsNames.Count; i++)
                {
                    ObjectList.Add(StageConfig.ObjectsNames[i]);
                }
                ScenePath = select.Result;
                UpdateDiscord("Editing " + select.Result);

            }
                catch (Exception ex)
                {
                    MessageBox.Show("Load failed. Error: " + ex.ToString());
                    return;
                }
            

            SetupLayerButtons();

            Background = new EditorBackground();

            entities = new EditorEntities(EditorScene);

            SetViewSize(SceneWidth, SceneHeight);

            UpdateControls();

            SceneLoaded = true;
        }

        private void OpenSceneManual()
        {
            string Result = null;
            OpenFileDialog open = new OpenFileDialog();
            open.Filter = "Scene File|*.bin";
            if (open.ShowDialog() != DialogResult.Cancel)
            {
                Result = open.FileName;
            }

            if (Result == null)
                return;

            UnloadScene();
            UseVisibilityPrefrences();

            try
            {
                if (File.Exists(Result))
                {
                    // Selected file
                    // Don't forget to populate these Members
                    string directoryPath = Path.GetDirectoryName(Result);
                    SelectedZone = new DirectoryInfo(directoryPath).Name;
                    SelectedScene = Path.GetFileName(Result);

                    StageTiles = new StageTiles(directoryPath);

                    SceneFilename = Result;
                }
                else
                {
                    SelectedZone = Result.Replace(Path.GetFileName(Result), "");
                    SelectedScene = Path.GetFileName(Result);

                    StageTiles = new StageTiles(Path.Combine(DataDirectory, "Stages", SelectedZone));
                    SceneFilename = Path.Combine(DataDirectory, "Stages", SelectedZone, SelectedScene);
                }

                //These cause issues, but not clearing them means when new stages are loaded Collision Mask 0 will be index 1024... (I think)
                CollisionLayerA.Clear();
                CollisionLayerB.Clear();

                for (int i = 0; i < 1024; i++)
                {
                    CollisionLayerA.Add(StageTiles.Config.CollisionPath1[i].DrawCMask(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(255, 255, 255, 255)));
                    CollisionLayerB.Add(StageTiles.Config.CollisionPath2[i].DrawCMask(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(255, 255, 255, 255)));
                }

                EditorScene = new EditorScene(SceneFilename);
                StageConfigFileName = Path.Combine(Path.GetDirectoryName(SceneFilename), "StageConfig.bin");
                if (File.Exists(StageConfigFileName))
                {
                    StageConfig = new StageConfig(StageConfigFileName);
                }

                ObjectList.Clear();
                for (int i = 0; i < GameConfig.ObjectsNames.Count; i++)
                {
                    ObjectList.Add(GameConfig.ObjectsNames[i]);
                }
                for (int i = 0; i < StageConfig.ObjectsNames.Count; i++)
                {
                    ObjectList.Add(StageConfig.ObjectsNames[i]);
                }
                ScenePath = Result;
                UpdateDiscord("Editing " + Result);

            }
            catch (Exception ex)
            {
                MessageBox.Show("Load failed. Error: " + ex.ToString());
                return;
            }


            SetupLayerButtons();

            Background = new EditorBackground();

            entities = new EditorEntities(EditorScene);

            SetViewSize(SceneWidth, SceneHeight);

            UpdateControls();

            SceneLoaded = true;
        }

        private void SetupLayerButtons()
        {
            TearDownExtraLayerButtons();
            foreach (EditorLayer el in EditorScene.OtherLayers)
            {
                ToolStripButton tsb = new ToolStripButton(el.Name);
                toolStrip1.Items.Add(tsb);
                tsb.ForeColor = Color.DarkGreen;
                tsb.CheckOnClick = true;
                tsb.Click += AdHocLayerEdit;

                _extraLayerButtons.Add(tsb);
            }

            UpdateDualButtonsControlsForLayer(FGLow, ShowFGLow, EditFGLow);
            UpdateDualButtonsControlsForLayer(FGHigh, ShowFGHigh, EditFGHigh);
            UpdateDualButtonsControlsForLayer(FGLower, ShowFGLower, EditFGLower);
            UpdateDualButtonsControlsForLayer(FGHigher, ShowFGHigher, EditFGHigher);
        }

        private void TearDownExtraLayerButtons()
        {
            foreach (var elb in _extraLayerButtons)
            {
                elb.Click -= AdHocLayerEdit;
                toolStrip1.Items.Remove(elb);
            }
            _extraLayerButtons.Clear();
        }



        /// <summary>
        /// Given a scene layer, configure the given visibiltiy and edit buttons which will control that layer.
        /// </summary>
        /// <param name="layer">The layer of the scene from which to extract a name.</param>
        /// <param name="visibilityButton">The button which controls the visibility of the layer.</param>
        /// <param name="editButton">The button which controls editing the layer.</param>
        private void UpdateDualButtonsControlsForLayer(EditorLayer layer, ToolStripButton visibilityButton, ToolStripButton editButton)
        {
            bool layerValid = layer != null;
            visibilityButton.Checked = layerValid;
            if (layerValid)
            {
                string name = layer.Name;
                visibilityButton.Text = name;
                editButton.Text = name;
            }
        }

        private void AdHocLayerEdit(object sender, EventArgs e)
        {
            ToolStripButton tsb = sender as ToolStripButton;
            Deselect(false);
            if (tsb.Checked)
            {
                if (!Properties.Settings.Default.KeepLayersVisible)
                {
                    ShowFGLow.Checked = false;
                    ShowFGHigh.Checked = false;
                    ShowFGLower.Checked = false;
                    ShowFGHigher.Checked = false;
                }
                EditFGLow.Checked = false;
                EditFGHigh.Checked = false;
                EditFGLower.Checked = false;
                EditFGHigher.Checked = false;
                EditEntities.Checked = false;

                foreach (var elb in _extraLayerButtons)
                {
                    if (elb != tsb)
                    {
                        elb.Checked = false;
                    }
                }
            }

            UpdateControls();
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            if (splitContainer1.Panel2.Controls.Count == 1)
            {
                splitContainer1.Panel2.Controls[0].Height = splitContainer1.Panel2.Height - 2;
                splitContainer1.Panel2.Controls[0].Width = splitContainer1.Panel2.Width - 2;
            }

            // TODO: It hides right now few pixels at the edge

            bool nvscrollbar = false;
            bool nhscrollbar = false;

            if (hScrollBar1.Maximum > viewPanel.Width - 2) nhscrollbar = true;
            if (vScrollBar1.Maximum > viewPanel.Height - 2) nvscrollbar = true;
            if (hScrollBar1.Maximum > viewPanel.Width - (nvscrollbar ? vScrollBar1.Width : 0)) hScrollBar1.Visible = true;
            if (vScrollBar1.Maximum > viewPanel.Height - (nhscrollbar ? hScrollBar1.Height : 0)) vScrollBar1.Visible = true;

            vScrollBar1.Visible = nvscrollbar;
            hScrollBar1.Visible = nhscrollbar;

            if (vScrollBar1.Visible)
            {
                // Docking isn't enough because we want that it will be high/wider when only one of the scroll bars is visible
                //vScrollBar1.Location = new Point(splitContainer1.SplitterDistance - 19, 0);
                vScrollBar1.Height = viewPanel.Height - (hScrollBar1.Visible ? hScrollBar1.Height : 0);
                vScrollBar1.LargeChange = vScrollBar1.Height;
                ScreenHeight = vScrollBar1.Height;
                hScrollBar1.Value = Math.Max(0, Math.Min(hScrollBar1.Value, hScrollBar1.Maximum - hScrollBar1.LargeChange));
            }
            else
            {
                ScreenHeight = GraphicPanel.Height;
                ShiftY = 0;
                vScrollBar1.Value = 0;
            }
            if (hScrollBar1.Visible)
            {
                //hScrollBar1.Location = new Point(0, splitContainer1.Height - 18);
                hScrollBar1.Width = viewPanel.Width - (vScrollBar1.Visible ? vScrollBar1.Width : 0);
                hScrollBar1.LargeChange = hScrollBar1.Width;
                ScreenWidth = hScrollBar1.Width;
                vScrollBar1.Value = Math.Max(0, Math.Min(vScrollBar1.Value, vScrollBar1.Maximum - vScrollBar1.LargeChange));
            }
            else
            {
                ScreenWidth = GraphicPanel.Width;
                ShiftX = 0;
                hScrollBar1.Value = 0;
            }

            if (hScrollBar1.Visible && vScrollBar1.Visible)
            {
                panel3.Visible = true;
                //panel3.Location = new Point(hScrollBar1.Width, vScrollBar1.Height);
            }
            else panel3.Visible = false;

            while (ScreenWidth > GraphicPanel.Width)
                ResizeGraphicPanel(GraphicPanel.Width * 2, GraphicPanel.Height);
            while (ScreenHeight > GraphicPanel.Height)
                ResizeGraphicPanel(GraphicPanel.Width, GraphicPanel.Height * 2);
        }


        private void SetViewSize(int width = 0, int height = 0)
        {
            vScrollBar1.Maximum = height;
            hScrollBar1.Maximum = width;

            GraphicPanel.DrawWidth = Math.Min(width, GraphicPanel.Width);
            GraphicPanel.DrawHeight = Math.Min(height, GraphicPanel.Height);

            Form1_Resize(null, null);

            hScrollBar1.Value = Math.Max(0, Math.Min(hScrollBar1.Value, hScrollBar1.Maximum - hScrollBar1.LargeChange));
            vScrollBar1.Value = Math.Max(0, Math.Min(vScrollBar1.Value, vScrollBar1.Maximum - vScrollBar1.LargeChange));
        }

        private void ResetViewSize()
        {
            SetViewSize((int)(SceneWidth * Zoom), (int)(SceneHeight * Zoom));
        }

        private void ResizeGraphicPanel(int width = 0, int height = 0)
        {
            GraphicPanel.Width = width;
            GraphicPanel.Height = height;

            GraphicPanel.ResetDevice();

            GraphicPanel.DrawWidth = Math.Min(hScrollBar1.Maximum, GraphicPanel.Width);
            GraphicPanel.DrawHeight = Math.Min(vScrollBar1.Maximum, GraphicPanel.Height);
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Open_Click(sender, e);
        }

        private void openDataDirectoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SceneChangeWarning(sender, e);
            if (AllowSceneChange == true || SceneLoaded == false)
            {
                AllowSceneChange = false;
                string newDataDirectory = GetDataDirectory();
                if (null == newDataDirectory) return;
                if (newDataDirectory.Equals(DataDirectory)) return;

                if (IsDataDirectoryValid(newDataDirectory))
                    ResetDataDirectoryToAndResetScene(newDataDirectory);
                else
                    MessageBox.Show($@"{newDataDirectory} is not
a valid Data Directory.",
                                    "Invalid Data Directory!",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Error);
            }
            else
            {
                return;
            }
            
        }

        public void OnResetDevice(object sender, DeviceEventArgs e)
        {
            Device device = e.Device;
        }

        private void GraphicPanel_OnRender(object sender, DeviceEventArgs e)
        {
            // hmm, if I call refresh when I update the values, for some reason it will stop to render until I stop calling refrsh
            // So I will refresh it here
            if (entitiesToolbar?.NeedRefresh ?? false) entitiesToolbar.PropertiesRefresh();
            if (EditorScene != null)
            {
                if (!IsTilesEdit())
                    Background.Draw(GraphicPanel);
                if (IsTilesEdit())
                    //Background.DrawEdit(GraphicPanel);
                if (EditorScene.OtherLayers.Contains(EditLayer))
                    EditLayer.Draw(GraphicPanel);
                if (ShowFGLower.Checked || EditFGLower.Checked)
                    FGLower.Draw(GraphicPanel);
                if (ShowFGLow.Checked || EditFGLow.Checked)
                    FGLow.Draw(GraphicPanel);
                if (ShowEntities.Checked && !EditEntities.Checked)
                    entities.Draw(GraphicPanel);
                if (ShowFGHigh.Checked || EditFGHigh.Checked)
                    FGHigh.Draw(GraphicPanel);
                if (ShowFGHigher.Checked || EditFGHigher.Checked)
                    FGHigher.Draw(GraphicPanel);
                if (EditEntities.Checked)
                    entities.Draw(GraphicPanel);



            }
            if (draggingSelection)
            {
                int x1 = (int)(selectingX / Zoom), x2 = (int)(lastX / Zoom);
                int y1 = (int)(selectingY / Zoom), y2 = (int)(lastY / Zoom);
                if (x1 != x2 && y1 != y2)
                {
                    if (x1 > x2)
                    {
                        x1 = (int)(lastX / Zoom);
                        x2 = (int)(selectingX / Zoom);
                    }
                    if (y1 > y2)
                    {
                        y1 = (int)(lastY / Zoom);
                        y2 = (int)(selectingY / Zoom);
                    }

                    GraphicPanel.DrawRectangle(x1, y1, x2, y2, Color.FromArgb(50, Color.Purple));

                    GraphicPanel.DrawLine(x1, y1, x2, y1, Color.Purple);
                    GraphicPanel.DrawLine(x1, y1, x1, y2, Color.Purple);
                    GraphicPanel.DrawLine(x2, y2, x2, y1, Color.Purple);
                    GraphicPanel.DrawLine(x2, y2, x1, y2, Color.Purple);
                }
            }
            if (scrolling)
            {
                if (vScrollBar1.Visible && hScrollBar1.Visible) GraphicPanel.Draw2DCursor(scrollPosition.X, scrollPosition.Y);
                else if (vScrollBar1.Visible) GraphicPanel.DrawVertCursor(scrollPosition.X, scrollPosition.Y);
                else if (hScrollBar1.Visible) GraphicPanel.DrawHorizCursor(scrollPosition.X, scrollPosition.Y);
            }
            if (showGrid)
                Background.DrawGrid(GraphicPanel);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            GraphicPanel.Init(this);
        }

        public void Run()
        {
            Show();
            Focus();
            GraphicPanel.Run();

        }

        private void LayerShowButton_Click(ToolStripButton button, string desc)
        {
            if (button.Checked)
            {
                button.Checked = false;
                button.ToolTipText = "Show " + desc;
            }
            else
            {
                button.Checked = true;
                button.ToolTipText = "Hide " + desc;
            }
        }

        private void ShowFGLow_Click(object sender, EventArgs e)
        {
            LayerShowButton_Click(ShowFGLow, "Layer FG Low");
        }

        private void ShowFGHigh_Click(object sender, EventArgs e)
        {
            LayerShowButton_Click(ShowFGHigh, "Layer FG High");
        }

        private void ShowFGHigher_Click(object sender, EventArgs e)
        {
            LayerShowButton_Click(ShowFGHigher, "Layer FG Higher");
        }

        private void ShowFGLower_Click(object sender, EventArgs e)
        {
            LayerShowButton_Click(ShowFGLower, "Layer FG Lower");
        }

        private void ShowEntities_Click(object sender, EventArgs e)
        {
            LayerShowButton_Click(ShowEntities, "Entities");
        }

        private void ShowAnimations_Click(object sender, EventArgs e)
        {
            LayerShowButton_Click(ShowAnimations, "Animations");
        }

        /// <summary>
        /// Deselects all tiles and entities
        /// </summary>
        /// <param name="updateControls">Whether to update associated on-screen controls</param>
        public void Deselect(bool updateControls = true)
        {
            if (IsEditing())
            {
                EditLayer?.Deselect();
                if (IsEntitiesEdit()) entities.Deselect();
                SetSelectOnlyButtonsState(false);
                if (updateControls)
                    UpdateEditLayerActions();
            }
            //MagnetDisable();
        }

        private void LayerEditButton_Click(ToolStripButton button)
        {
            Deselect(false);
            if (button.Checked)
            {
                button.Checked = false;
            }
            else
            {
                EditFGLow.Checked = false;
                EditFGHigh.Checked = false;
                EditFGLower.Checked = false;
                EditFGHigher.Checked = false;
                EditEntities.Checked = false;
                button.Checked = true;
            }

            foreach (var elb in _extraLayerButtons)
            {
                elb.Checked = false;
            }
            UpdateControls();
        }

        private void EditFGLow_Click(object sender, EventArgs e)
        {
            LayerEditButton_Click(EditFGLow);
        }

        private void EditFGHigh_Click(object sender, EventArgs e)
        {
            LayerEditButton_Click(EditFGHigh);
        }

        private void EditFGLower_Click(object sender, EventArgs e)
        {
            LayerEditButton_Click(EditFGLower);
        }

        private void EditFGHigher_Click(object sender, EventArgs e)
        {
            LayerEditButton_Click(EditFGHigher);
        }

        private void EditEntities_Click(object sender, EventArgs e)
        {
            LayerEditButton_Click(EditEntities);
        }


        private void Save_Click(object sender, EventArgs e)
        {
            if (EditorScene == null) return;

            if (IsTilesEdit())
            {
                // Apply changes
                Deselect();
            }

            try
            {
                EditorScene.Save(SceneFilename);
            }
            catch (Exception ex)
            {
                ShowError($@"Failed to save the scene to file '{SceneFilename}'
Error: {ex.Message}");
            }

            try
            { 
                StageConfig?.Write(StageConfigFileName);
            }
            catch (Exception ex)
            {
                ShowError($@"Failed to save the StageConfig to file '{StageConfigFileName}'
Error: {ex.Message}");
            }
        }

        private void ShowError(string message, string title = "Error!")
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void MagnetMode_Click(object sender, EventArgs e)
        {
        }


        private void New_Click(object sender, EventArgs e)
        {
        }

        private void newToolStripMenuItem_Click(object sender, EventArgs e)
        {
            New_Click(sender, e);
        }

        private void sToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Save_Click(sender, e);
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }
        private void saveAspngToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (EditorScene == null) return;

            SaveFileDialog save = new SaveFileDialog();
            save.Filter = ".png File|*.png";
            save.DefaultExt = "png";
            if (save.ShowDialog() != DialogResult.Cancel)
            {
                using (Bitmap bitmap = new Bitmap(SceneWidth, SceneHeight))
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    // not all scenes have both a Low and a High foreground
                    // only attempt to render the ones we actually have
                    FGLow?.Draw(g);
                    FGHigh?.Draw(g);
                    FGLower?.Draw(g);
                    FGHigher?.Draw(g);
                    bitmap.Save(save.FileName);
                }
            }
        }

        private void exportEachLayerAspngToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                if (EditorScene?.Layers == null || !EditorScene.Layers.Any()) return;

                var dialog = new FolderSelectDialog()
                {
                    Title = "Select folder to save each exported layer image to"
                };

                if (!dialog.ShowDialog()) return;

                int fileCount = 0;

                foreach (var editorLayer in EditorScene.AllLayers)
                {
                    string fileName = Path.Combine(dialog.FileName, editorLayer.Name + ".png");

                    if (!CanWriteFile(fileName))
                    {
                        ShowError($"Layer export aborted. {fileCount} images saved.");
                        return;
                    }

                    using (var bitmap = new Bitmap(editorLayer.Width * EditorLayer.TILE_SIZE, editorLayer.Height * EditorLayer.TILE_SIZE))
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        editorLayer.Draw(g);
                        bitmap.Save(fileName, ImageFormat.Png);
                        ++fileCount;
                    }
                }

                MessageBox.Show($"Layer export succeeded. {fileCount} images saved.", "Success!",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ShowError("An error occurred: " + ex.Message);
            }
        }

        private bool CanWriteFile(string fullFilePath)
        {
            if (!File.Exists(fullFilePath)) return true;

            if (File.GetAttributes(fullFilePath).HasFlag(FileAttributes.ReadOnly))
            {
                ShowError($"The file '{fullFilePath}' is Read Only.", "File is Read Only.");
                return false;
            }

            var result = MessageBox.Show($"The file '{fullFilePath}' already exists. Overwrite?", "Overwrite?",
                                         MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (result == DialogResult.Yes) return true;

            return false;
        }

        private void splitContainer1_SplitterMoved(object sender, SplitterEventArgs e)
        {
            Form1_Resize(null, null);
        }

        private void deleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DeleteSelected();
        }

        private void flipHorizontalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            EditLayer?.FlipPropertySelected(FlipDirection.Horizontal);
            UpdateEditLayerActions();
        }


        private void copyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (IsTilesEdit())
                CopyTilesToClipboard();
															 
			 
            else if (IsEntitiesEdit())
                CopyEntitiesToClipboard();

			 
            UpdateControls();
        }

        private void duplicateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (IsTilesEdit())
            {
                EditLayer.PasteFromClipboard(new Point(16, 16), EditLayer.CopyToClipboard(true));
                UpdateEditLayerActions();
            }
            else if (IsEntitiesEdit())
            {
                try
                {
                    entities.PasteFromClipboard(new Point(16, 16), entities.CopyToClipboard(true));
                    UpdateLastEntityAction();
                }
                catch (EditorEntities.TooManyEntitiesException)
                {
                    MessageBox.Show("Too many entities! (limit: 2048)");
                    return;
                }
                UpdateEntitiesToolbarList();
                SetSelectOnlyButtonsState();
            }
        }

        private void undoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            EditorUndo();
        }

        private void redoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            EditorRedo();
        }

        private void undoButton_Click(object sender, EventArgs e)
        {
            EditorUndo();
        }

        private void redoButton_Click(object sender, EventArgs e)
        {
            EditorRedo();
        }

        public void EditorUndo()
        {
            if (undo.Count > 0)
            {
                if (IsTilesEdit())
                {
                    // Deselect to apply the changes
                    Deselect();
                }
                else if (IsEntitiesEdit())
                {
                    if (undo.Peek() is ActionAddDeleteEntities)
                    {
                        // deselect only if delete/create
                        Deselect();
                    }
                }
                IAction act = undo.Pop();
                act.Undo();
                redo.Push(act.Redo());
                if (IsEntitiesEdit() && IsSelected())
                {
                    // We need to update the properties of the selected entity
                    entitiesToolbar.UpdateCurrentEntityProperites();
                }
            }
            UpdateControls();
        }

        public void EditorRedo()
        {
            if (redo.Count > 0)
            {
                IAction act = redo.Pop();
                act.Undo();
                undo.Push(act.Redo());
                if (IsEntitiesEdit() && IsSelected())
                {
                    // We need to update the properties of the selected entity
                    entitiesToolbar.UpdateCurrentEntityProperites();
                }
            }
            UpdateControls();
        }

        private void UpdateTooltips()
        {
            UpdateTooltipForStacks(undoButton, undo);
            UpdateTooltipForStacks(redoButton, redo);
        }

        private void UpdateTooltipForStacks(ToolStripButton tsb, Stack<IAction> actionStack)
        {
            if (actionStack?.Count > 0)
            {
                IAction action = actionStack.Peek();
                tsb.ToolTipText = string.Format(tsb.Text, action.Description + " ");
            }
            else
            {
                tsb.ToolTipText = string.Format(tsb.Text, string.Empty);
            }
        }

        private void cutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (IsTilesEdit())
            {
                CopyTilesToClipboard();
                DeleteSelected();
                UpdateControls();
                UpdateEditLayerActions();
            }
            else if (IsEntitiesEdit())
            {
                CopyEntitiesToClipboard();
                DeleteSelected();
                UpdateControls();
            }
        }

        private void CopyTilesToClipboard()
        {
            Dictionary<Point, ushort> copyData = EditLayer.CopyToClipboard();

            // Make a DataObject for the copied data and send it to the Windows clipboard for cross-instance copying
            if (Properties.Settings.Default.EnableWindowsClipboard)
                Clipboard.SetDataObject(new DataObject("ManiacTiles", copyData), true);

            // Also copy to Maniac's clipboard in case it gets overwritten elsewhere
            TilesClipboard = copyData;
        }

        private void CopyEntitiesToClipboard()
        {
            List<EditorEntity> copyData = entities.CopyToClipboard();

            // Make a DataObject for the copied data and send it to the Windows clipboard for cross-instance copying
            if (Properties.Settings.Default.EnableWindowsClipboard)
            {
                if (!Properties.Settings.Default.ProhibitEntityUseOnExternalClipboard)
                    Clipboard.SetDataObject(new DataObject("ManiacEntities", copyData), true);
            }
                

            // Also copy to Maniac's clipboard in case it gets overwritten elsewhere
            entitiesClipboard = copyData;
        }

        private void pasteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (IsTilesEdit())
            {
                // check if there are tiles on the Windows clipboard; if so, use those
                if (Properties.Settings.Default.EnableWindowsClipboard && Clipboard.ContainsData("ManiacTiles"))
                {
                    EditLayer.PasteFromClipboard(new Point((int)(lastX / Zoom) + EditorLayer.TILE_SIZE - 1, (int)(lastY / Zoom) + EditorLayer.TILE_SIZE - 1), (Dictionary<Point, ushort>)Clipboard.GetDataObject().GetData("ManiacTiles"));
                    UpdateEditLayerActions();
                }

                // if there's none, use the internal clipboard
                else if (TilesClipboard != null)
                {
                    EditLayer.PasteFromClipboard(new Point((int)(lastX / Zoom) + EditorLayer.TILE_SIZE - 1, (int)(lastY / Zoom) + EditorLayer.TILE_SIZE - 1), TilesClipboard);
                    UpdateEditLayerActions();
                }
            }
            else if (IsEntitiesEdit())
            {
                try
                {
                    // check if there are entities on the Windows clipboard; if so, use those
                    if (Properties.Settings.Default.EnableWindowsClipboard && Clipboard.ContainsData("ManiacEntities"))
                    {
                        entities.PasteFromClipboard(new Point((int)(lastX / Zoom), (int)(lastY / Zoom)), (List<EditorEntity>)Clipboard.GetDataObject().GetData("ManiacEntities"));
                        UpdateLastEntityAction();
                    }

                    // if there's none, use the internal clipboard
                    else if (entitiesClipboard != null)
                    {
                        entities.PasteFromClipboard(new Point((int)(lastX / Zoom), (int)(lastY / Zoom)), entitiesClipboard);
                        UpdateLastEntityAction();
                    }
                }
                catch (EditorEntities.TooManyEntitiesException)
                {
                    MessageBox.Show("Too many entities! (limit: 2048)");
                    return;
                }
                UpdateEntitiesToolbarList();
                SetSelectOnlyButtonsState();
            }
        }

        private void GraphicPanel_MouseEnter(object sender, EventArgs e)
        {
            //GraphicPanel.Focus();
        }

        private void GraphicPanel_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(Int32)) && IsTilesEdit())
            {
                Point rel = GraphicPanel.PointToScreen(Point.Empty);
                e.Effect = DragDropEffects.Move;
                // (ushort)((Int32)e.Data.GetData(e.Data.GetFormats()[0])
                EditLayer.StartDragOver(new Point((int)(((e.X - rel.X) + ShiftX) / Zoom), (int)(((e.Y - rel.Y) + ShiftY) / Zoom)), (ushort)TilesToolbar.SelectedTile);
                UpdateEditLayerActions();
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void GraphicPanel_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(Int32)) && IsTilesEdit())
            {
                Point rel = GraphicPanel.PointToScreen(Point.Empty);
                EditLayer.DragOver(new Point((int)(((e.X - rel.X) + ShiftX) / Zoom), (int)(((e.Y - rel.Y) + ShiftY) / Zoom)), (ushort)TilesToolbar.SelectedTile);
                GraphicPanel.Render();
            }
        }

        private void GraphicPanel_DragLeave(object sender, EventArgs e)
        {
            EditLayer?.EndDragOver(true);
            GraphicPanel.Render();
        }

        private void GraphicPanel_DragDrop(object sender, DragEventArgs e)
        {
            EditLayer?.EndDragOver(false);
        }

        private void zoomInButton_Click(object sender, EventArgs e)
        {
            ZoomLevel += 1;
            if (ZoomLevel > 5) ZoomLevel = 5;
            if (ZoomLevel < -5) ZoomLevel = -5;

            SetZoomLevel(ZoomLevel, new Point(0, 0));
        }

        private void zoomOutButton_Click(object sender, EventArgs e)
        {
            ZoomLevel -= 1;
            if (ZoomLevel > 5) ZoomLevel = 5;
            if (ZoomLevel < -5) ZoomLevel = -5;

            SetZoomLevel(ZoomLevel, new Point(0, 0));
        }

        private void selectTool_Click(object sender, EventArgs e)
        {
            selectTool.Checked = !selectTool.Checked;
            pointerButton.Checked = false;
            placeTilesButton.Checked = false;
            UpdateControls();
        }

        private void pointerButton_Click(object sender, EventArgs e)
        {
            pointerButton.Checked = !pointerButton.Checked;
            selectTool.Checked = false;
            placeTilesButton.Checked = false;
            UpdateControls();
        }

        private void placeTilesButton_Click(object sender, EventArgs e)
        {
            placeTilesButton.Checked = !placeTilesButton.Checked;
            selectTool.Checked = false;
            pointerButton.Checked = false;
            UpdateControls();
        }

        private void MapEditor_Activated(object sender, EventArgs e)
        {
            GraphicPanel.Focus();
        }

        private void MapEditor_KeyDown(object sender, KeyEventArgs e)
        {
            if (!GraphicPanel.Focused && e.Control)
            {
                GraphicPanel_OnKeyDown(sender, e);
            }
        }

        private void saveAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (EditorScene == null) return;

            if (IsTilesEdit())
            {
                // Apply changes
                Deselect();
            }

            SaveFileDialog save = new SaveFileDialog();
            save.Filter = "Scene File|*.bin";
            save.DefaultExt = "bin";
            save.InitialDirectory = Path.GetDirectoryName(SceneFilename);
            save.RestoreDirectory = false;
            save.FileName = Path.GetFileName(SceneFilename);
            if (save.ShowDialog() != DialogResult.Cancel)
            {
                EditorScene.Write(save.FileName);
            }
        }

        private void importObjectsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                Scene sourceScene = GetSceneSelection();
                if (null == sourceScene) return;

                using (var objectImporter = new ObjectImporter(sourceScene.Objects, EditorScene.Objects, StageConfig))
                {
                    if (objectImporter.ShowDialog() != DialogResult.OK)
                        return; // nothing to do

                    // user clicked Import, get to it!
                    UpdateControls();
                    entitiesToolbar?.RefreshObjects(EditorScene.Objects);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to import Objects. " + ex.Message);
            }
        }

        private void importSoundsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                StageConfig sourceStageConfig = null;
                using (var fd = new OpenFileDialog())
                {
                    fd.Filter = "Stage Config File|*.bin";
                    fd.DefaultExt = ".bin";
                    fd.Title = "Select Stage Config File";
                    fd.InitialDirectory = Path.Combine(DataDirectory, "Stages");
                    if (fd.ShowDialog() == DialogResult.OK)
                    {
                        sourceStageConfig = new StageConfig(fd.FileName);
                    }
                }
                if (null == sourceStageConfig) return;

                using (var soundImporter = new SoundImporter(sourceStageConfig, StageConfig))
                {
                    if (soundImporter.ShowDialog() != DialogResult.OK)
                        return; // nothing to do

                    // changing the sound list doesn't require us to do anything either
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to import sounds. " + ex.Message);
            }
        }

        private Scene GetSceneSelection()
        {
            string selectedScene;
            using (SceneSelect select = new SceneSelect(GameConfig))
            {
                select.ShowDialog();

                if (select.Result == null)
                    return null;

                selectedScene = select.Result;
            }

            if (!File.Exists(selectedScene))
            {
                string[] splitted = selectedScene.Split('/');

                string part1 = splitted[0];
                string part2 = splitted[1];

                selectedScene = Path.Combine(DataDirectory, "Stages", part1, part2);
            }
            return new Scene(selectedScene);
        }

        private void layerManagerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Deselect(true);

            using (var lm = new LayerManager(EditorScene))
            {
                lm.ShowDialog();
            }
            controlWindowOpen = true;

            SetupLayerButtons();
            ResetViewSize();
            UpdateControls();
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var aboutBox = new AboutBox())
            {
                aboutBox.ShowDialog();
            }
        }

        private void optionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var optionBox = new OptionBox())
            {
                optionBox.ShowDialog();
            }
        }

        private void controlsToolStripMenuItem_Click(object sender, EventArgs e)
        {

            using (var ControlBox = new controlBox())
            {
                ControlBox.ShowDialog();
            }
        }

        private void Editor_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                var mySettings = Properties.Settings.Default;
                mySettings.IsMaximized = WindowState == FormWindowState.Maximized;
                mySettings.Save();
            }
            catch (Exception ex)
            {
                Debug.Write("Failed to write settings: " + ex);
            }
        }

        private void ReloadToolStripButton_Click(object sender, EventArgs e)
        {
            try
            {
                // release all our resources, and force a reload of the tiles
                // Entities should take care of themselves
                DisposeTextures();
                EditorEntity.ReleaseResources();

                StageTiles?.Image.Reload();
                TilesToolbar?.Reload();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void RunScene_Click(object sender, EventArgs e)
        {
            // Ask where Sonic Mania ia located when not set
            if (string.IsNullOrEmpty(Properties.Settings.Default.RunGamePath))
            {
                var ofd = new OpenFileDialog();
                ofd.Title = "Select SonicMania.exe";
                ofd.Filter = "Windows PE Executable|*.exe";
                if (ofd.ShowDialog() == DialogResult.OK)
                    Properties.Settings.Default.RunGamePath = ofd.FileName;
            }
            else
            {
                if (!File.Exists(Properties.Settings.Default.RunGamePath))
                {
                    Properties.Settings.Default.RunGamePath = "";
                    return;
                }
            }

            ProcessStartInfo psi;

            if (Properties.Settings.Default.RunGameInsteadOfScene == true)
            {
                psi = new ProcessStartInfo(Properties.Settings.Default.RunGamePath);
            }
            else
            {
                if (Properties.Settings.Default.UsePrePlusOffsets == true)
                {
                    psi = new ProcessStartInfo(Properties.Settings.Default.RunGamePath, $"stage={SelectedZone};scene={SelectedScene[5]};");
                }
                else
                {
                    psi = new ProcessStartInfo(Properties.Settings.Default.RunGamePath, $"stage={SelectedZone};scene={SelectedScene[5]};");
                    // TODO: Find workaround to get Mania to boot into a Scene Post Plus
                }

            }
            if (Properties.Settings.Default.RunGamePath != "")
            {
                string maniaDir = Path.GetDirectoryName(Properties.Settings.Default.RunGamePath);
                // Check if the mod loader is installed
                if (File.Exists(Path.Combine(maniaDir, "d3d9.dll")))
                    psi.WorkingDirectory = maniaDir;
                else
                    psi.WorkingDirectory = Path.GetDirectoryName(DataDirectory);
                var p = Process.Start(psi);
                GameRunning = true;
                UpdateControls();
                // Patches
                GameMemory.Attach(p);
                // Disable Background Pausing
                GameMemory.WriteByte(0x005FDD00, 0xEB);
                // Enable Debug
                GameMemory.WriteByte(0x00E48768, 0x01);
                // Enable DevMenu
                GameMemory.WriteByte(0x006F1806, 0x01);



                new Thread(() =>
                {
                    /* Level != Main Menu*/
                    while (GameMemory.ReadByte(0x00CCF6F8) != 0x02)
                    {
                        // Check if the user closed the game
                        if (p.HasExited)
                        {
                            GameRunning = false;
                            Invoke(new Action(() => UpdateControls()));
                            return;
                        }
                        // Restrict to Player 1
                        if (GameMemory.ReadByte(0xA4C860) == 0x01)
                        {
                            GameMemory.WriteByte(0xA4C860, 0x00);
                        }
                        Thread.Sleep(300);
                    }
                    // User is on the Main Menu
                    // Close the game
                    GameMemory.WriteByte(0x628094, 0);
                    GameRunning = false;
                    Invoke(new Action(() => UpdateControls()));
                }).Start();
            }

        }

        private void statusStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {

        }

        private void toolStripStatusLabel2_Click(object sender, EventArgs e)
        {

        }

        private void helpToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void toolStrip3_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {

        }

        private void amountInSelectionLabel_Click(object sender, EventArgs e)
        {

        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void pixelModeButton_Click(object sender, EventArgs e)
        {
            if (pixelModeButton.Checked == false)
            {
                pixelModeButton.Checked = true;
                Properties.Settings.Default.pixelCountMode = true;
            }
            else
            {
                pixelModeButton.Checked = false;
                Properties.Settings.Default.pixelCountMode = false;
            }

        }

        private void scrollLockButton_Click(object sender, EventArgs e)
        {
            if (scrollLockButton.Checked == false)
            {
                scrollLockButton.Checked = true;
                Properties.Settings.Default.scrollLock = true;
                scrollDirection = "Locked";
            }
            else
            {
                if (Properties.Settings.Default.ScrollLockY == true)
                {
                    scrollLockButton.Checked = false;
                    Properties.Settings.Default.scrollLock = false;
                    scrollDirection = "Y";
                }
                else
                {
                    scrollLockButton.Checked = false;
                    Properties.Settings.Default.scrollLock = false;
                    scrollDirection = "X";
                }

            }

        }

        private void MapEditor_KeyUp(object sender, KeyEventArgs e)
        {
            if (!GraphicPanel.Focused && e.Control)
            {
                GraphicPanel_OnKeyUp(sender, e);
            }
        }

        private void flipVerticalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            EditLayer?.FlipPropertySelected(FlipDirection.Veritcal);
            UpdateEditLayerActions();
        }

        private void resetDeviceButton_Click(object sender, EventArgs e)
        {
            GraphicPanel.ResetDevice();
        }

        private void vScrollBar1_Scroll(object sender, ScrollEventArgs e)
        {
            ShiftY = e.NewValue;
            GraphicPanel.Render();
        }

        private void hScrollBar1_Scroll(object sender, ScrollEventArgs e)
        {
            ShiftX = e.NewValue;
            GraphicPanel.Render();
        }

        private void vScrollBar1_ValueChanged(object sender, EventArgs e)
        {
            ShiftY = (sender as VScrollBar).Value;
            if (!(zooming || draggingSelection || dragged || scrolling)) GraphicPanel.Render();

            if (draggingSelection)
            {
                GraphicPanel.OnMouseMoveEventCreate();
            }
        }

        private void hScrollBar1_ValueChanged(object sender, EventArgs e)
        {
            ShiftX = hScrollBar1.Value;
            if (!(zooming || draggingSelection || dragged || scrolling)) GraphicPanel.Render();
        }

        private void showTileIDButton_Click(object sender, EventArgs e)
        {
            if (showTileIDButton.Checked == false)
            {
                showTileIDButton.Checked = true;
                ReloadToolStripButton_Click(sender, e);
                showTileID = true;
            }
            else
            {
                showTileIDButton.Checked = false;
                ReloadToolStripButton_Click(sender, e);
                showTileID = false;
            }
        }

        private void showGridButton_Click(object sender, EventArgs e)
        {
            if (showGridButton.Checked == false)
            {
                showGridButton.Checked = true;
                showGrid = true;
            }
            else
            {
                showGridButton.Checked = false;
                showGrid = false;
            }
        }

        private void ShowCollisionAButton_Click(object sender, EventArgs e)
        {
            if (showCollisionAButton.Checked == false)
            {
                showCollisionAButton.Checked = true;
                showCollisionA = true;
                showCollisionBButton.Checked = false;
                showCollisionB = false;
                ReloadToolStripButton_Click(sender, e);
            }
            else
            {
                showCollisionAButton.Checked = false;
                showCollisionA = false;
                showCollisionBButton.Checked = false;
                showCollisionB = false;
                ReloadToolStripButton_Click(sender, e);
            }
        }

        private void showCollisionBButton_Click(object sender, EventArgs e)
        {
            if (showCollisionBButton.Checked == false)
            {
                showCollisionBButton.Checked = true;
                showCollisionB = true;
                showCollisionAButton.Checked = false;
                showCollisionA = false;
                ReloadToolStripButton_Click(sender, e);
            }
            else
            {
                showCollisionBButton.Checked = false;
                showCollisionB = false;
                showCollisionAButton.Checked = false;
                showCollisionA = false;
                ReloadToolStripButton_Click(sender, e);
            }
        }

        private void nudgeFasterButton_Click(object sender, EventArgs e)
        {
            if (nudgeFasterButton.Checked == false)
            {
                nudgeFasterButton.Checked = true;
                Properties.Settings.Default.EnableFasterNudge = true;
            }
            else
            {
                nudgeFasterButton.Checked = false;
                Properties.Settings.Default.EnableFasterNudge = false;
            }
        }

        private void removeObjectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {

                using (var ObjectRemover = new ObjectRemover(EditorScene.Objects, StageConfig))
                {
                    if (ObjectRemover.ShowDialog() != DialogResult.OK)
                        return; // nothing to do

                    // user clicked Import, get to it!
                    UpdateControls();
                    entitiesToolbar?.RefreshObjects(EditorScene.Objects);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to import Objects. " + ex.Message);
            }
        }

        private void vScrollBar1_Entered(object sender, EventArgs e)
        {
            if (Properties.Settings.Default.scrollLock == false)
            {
                scrollDirection = "Y";
            }
            else
            {
                scrollDirection = "Locked";
            }
            
        }
        private void hScrollBar1_Entered(object sender, EventArgs e)
        {
            if (Properties.Settings.Default.scrollLock == false)
            {
                scrollDirection = "X";
            }
            else
            {
                scrollDirection = "Locked";
            }
        }
        public void DisposeTextures()
        {
            // Make sure to dispose the textures of the extra layers too
            StageTiles?.DisposeTextures();
            if (FGHigh != null) FGHigh.DisposeTextures();
            if (FGLow != null) FGLow.DisposeTextures();
            if (FGHigher != null) FGHigh.DisposeTextures();
            if (FGLower != null) FGLow.DisposeTextures();
            //CollisionLayerA.Clear();
            //CollisionLayerB.Clear();
            foreach (var el in EditorScene.OtherLayers)
            {
                el.DisposeTextures();
            }
        }

        public Rectangle GetScreen()
        {
            return new Rectangle(ShiftX, ShiftY, viewPanel.Width, viewPanel.Height);
        }

        public double GetZoom()
        {
            return Zoom;
        }

        DialogResult deviceExceptionResult;
        public void DeviceExceptionDialog()
        {
            using (var deviceLostBox = new DeviceLostBox())
            {
                deviceLostBox.ShowDialog();
                deviceExceptionResult = deviceLostBox.DialogResult;
            }
            if (deviceExceptionResult == DialogResult.Yes)
            {
                GraphicPanel.Dispose();
                //GraphicPanel.ResetDevice();
            }
            else
            {
                
            }
        }
        private void Editor_FormClosing1(object sender, System.Windows.Forms.FormClosingEventArgs e)
        {
            if (SceneLoaded == true && Properties.Settings.Default.DisableSaveWarnings == false)
            {
                DialogResult exitBoxResult;
                using (var exitBox = new ExitWarningBox())
                {
                    exitBox.ShowDialog();
                    exitBoxResult = exitBox.DialogResult;
                }
                if (exitBoxResult == DialogResult.Yes)
                {
                    Save_Click(sender, e);
                    Environment.Exit(1);
                }
                else if (exitBoxResult == DialogResult.No)
                {
                    Environment.Exit(1);
                }
                else
                {
                    e.Cancel = true;
                }
            }
            else
            {
                Environment.Exit(1);
            }

        }
        private void SceneChangeWarning(object sender, EventArgs e)
        {
            if (SceneLoaded == true && Properties.Settings.Default.DisableSaveWarnings == false)
            {
                DialogResult exitBoxResult;
                using (var exitBox = new ExitWarningBox())
                {
                    exitBox.ShowDialog();
                    exitBoxResult = exitBox.DialogResult;
                }
                if (exitBoxResult == DialogResult.Yes)
                {
                    Save_Click(sender, e);
                    AllowSceneChange = true;
                }
                else if (exitBoxResult == DialogResult.No)
                {
                    AllowSceneChange = true;
                }
                else
                {
                    AllowSceneChange = false;
                }
            }
            else
            {
                AllowSceneChange = true;
            }

        }
    }
}