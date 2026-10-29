using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace Dungeon
{
    public enum EnemyKind { Rat, Bat, Slime, Skeleton }
    public enum ItemKind { Potion, Gold, Sword, Heart, Shield, Oil, Ring, Gem, Whetstone, Bread, Regen, Hourglass, Boots, Map, Bomb }

    public sealed class Enemy
    {
        public int X, Y;
        public EnemyKind Kind;
        public int Hp, MaxHp, Dmg, MoveEvery, MoveCounter;
        public float Rx, Ry;    // interpolated render position
        public float Flash;     // hit-flash timer
        public string Name;
    }

    public sealed class Item
    {
        public int X, Y;
        public ItemKind Kind;
        public int Value;
    }

    /// <summary>
    /// The whole game: state, rules, and rendering. It owns no window — a platform
    /// host (desktop GameWindow, iOS GLKView) creates the GL context, sets
    /// <see cref="FramebufferSize"/>, and forwards frames, keys, and pointer events.
    /// </summary>
    public sealed partial class Game
    {
        // ---- host interface -------------------------------------------------
        /// <summary>Drawable size in pixels; set by the host before each frame.</summary>
        public Vector2i FramebufferSize;
        /// <summary>Unsafe screen edges in pixels (notch, home indicator): left, top, right, bottom.</summary>
        public Vector4 SafeInsets;
        /// <summary>Touch-first UI: swipe controls, on-screen buttons and keyboard, no Quit item.</summary>
        public readonly bool TouchUI;
        /// <summary>Raised when the game wants the app to exit (menu Quit, capture finished).</summary>
        public event Action QuitRequested;
        readonly IAudioOut _audioOut;

        // ---- rendering resources -------------------------------------------
        Shader _world, _sprite;
        Mesh _floor, _bill;                 // single floor tile (exit portal) + sprite quad
        Mesh _wallsMesh, _floorMesh, _ceilMesh; // whole-level geometry, rebuilt per level
        GlTexture _wallTex, _floorTex, _ceilTex, _exitTex;
        GlTexture _wallNrm, _floorNrm, _ceilNrm, _flatNrm;
        Dictionary<EnemyKind, GlTexture> _enemyTex;
        Dictionary<ItemKind, GlTexture> _itemTex;
        Font _font;
        Hud _hud;
        Audio _audio;
        Task<short[]> _musicTask; // per-level track generated off the main thread
        readonly List<(int x, int y)> _exits = new();

        // ---- game state -----------------------------------------------------
        readonly Random _rng = new(20260729);
        Level _level;
        int _depth = 1;
        int _px, _py, _dir;             // player grid pos + facing (0=N,1=E,2=S,3=W)
        int _hp = 24, _maxHp = 24, _atkMin = 2, _atkMax = 4, _gold;
        int _shield;                    // remaining hits the shield halves before it breaks
        int _torchTurns;                // brighter torch while > 0
        int _atkTurns, _atkBonus;       // temporary attack bonus (whetstone)
        int _regenTurns;                // heal 1 HP per turn while > 0
        int _freeze;                    // enemies frozen (hourglass) while > 0
        int _haste;                     // enemies act at half speed (boots) while > 0
        long _turnNo;                   // monotonically increasing turn counter
        bool[,] _seen;

        // ---- hero (built at character creation) -----------------------------
        string _name = "";
        int _str, _dex, _vit, _luk;     // base attributes
        int _dodgePct, _critPct;        // derived from DEX / LUK (+ class)
        string _className = "";
        int _createSel;                 // which attribute row is selected (point-buy)
        const int PbMin = 3, PbMax = 18, PbTotal = 44; // point-buy budget

        readonly List<Enemy> _enemies = new();
        readonly List<Item> _items = new();

        // ---- movement animation --------------------------------------------
        bool _anim;
        float _animT, _animDur = 0.11f;
        float _fromCx, _fromCz, _toCx, _toCz, _fromYaw, _toYaw;
        float _rcx, _rcz, _ryaw, _targetYaw;
        bool _pendingDescend;

        // ---- fx / ui --------------------------------------------------------
        float _time, _screenFlash, _descendFlash, _intro, _levelBanner;
        bool _dead;
        struct Msg { public string Text; public Vector4 Color; public float Age; }
        readonly List<Msg> _log = new();

        // ---- menu / character creation --------------------------------------
        enum MenuAction { Continue, NewGame, Shop, Leaderboards, Quit }
        enum CreateAction { Reroll, Begin, Back }
        enum HeroClass { Warrior, Rogue, Guardian, Trickster }
        bool _inMenu = true;
        bool _inCreate;
        int _menuSel;
        readonly List<MenuAction> _menuItems = new();
        SaveData _menuSave; // cached save shown/used by the Continue entry

        static readonly int[] DX = { 0, 1, 0, -1 };
        static readonly int[] DY = { -1, 0, 1, 0 };

        // ---- screenshot capture (headless, deterministic) -------------------
        bool _capture; string _capDir; int _capShot, _capSettle;
        static readonly string[] CapShots = { "menu", "hero", "explore", "depths" };

        public Game(IAudioOut audioOut, bool touchUI)
        {
            _audioOut = audioOut;
            TouchUI = touchUI;
        }

        void Close() => QuitRequested?.Invoke();

        /// <summary>Enable auto-capture mode: boot through a fixed set of scenes, save a BMP of each, then quit.</summary>
        public void EnableCapture(string dir) { _capture = true; _capDir = dir; }

        /// <summary>Icon mode: render one staged, HUD-free dungeon frame to a BMP, then quit.</summary>
        public void EnableIconCapture(string path) { _capture = true; _iconPath = path; _capDir = Path.GetDirectoryName(Path.GetFullPath(path)); }
        string _iconPath;

        // =====================================================================
        /// <summary>Create GL resources and audio. Call once with the GL context current.</summary>
        public void Load()
        {
            GL.ClearColor(0.02f, 0.02f, 0.03f, 1f);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _world = new Shader(WorldVS, WorldFS);
            _sprite = new Shader(WorldVS, SpriteFS);
            _floor = new Mesh(Mesh.FloorVerts());
            _bill = Mesh.Billboard();

            var wall = Textures.StoneWall(_depth);
            var floor = Textures.Floor(_depth);
            var ceil = Textures.Ceiling(_depth);
            _wallTex = wall.Albedo; _wallNrm = wall.Normal;
            _floorTex = floor.Albedo; _floorNrm = floor.Normal;
            _ceilTex = ceil.Albedo; _ceilNrm = ceil.Normal;
            _flatNrm = Textures.FlatNormal();
            _exitTex = Textures.Exit();
            _enemyTex = new()
            {
                [EnemyKind.Rat] = Textures.Rat(),
                [EnemyKind.Bat] = Textures.Bat(),
                [EnemyKind.Slime] = Textures.Slime(),
                [EnemyKind.Skeleton] = Textures.Skeleton(),
            };
            _itemTex = new()
            {
                [ItemKind.Potion] = Textures.Potion(),
                [ItemKind.Gold] = Textures.Gold(),
                [ItemKind.Sword] = Textures.Sword(),
                [ItemKind.Heart] = Textures.Heart(),
                [ItemKind.Shield] = Textures.Shield(),
                [ItemKind.Oil] = Textures.Oil(),
                [ItemKind.Ring] = Textures.Ring(),
                [ItemKind.Gem] = Textures.Gem(),
                [ItemKind.Whetstone] = Textures.Whetstone(),
                [ItemKind.Bread] = Textures.Bread(),
                [ItemKind.Regen] = Textures.Regen(),
                [ItemKind.Hourglass] = Textures.Hourglass(),
                [ItemKind.Boots] = Textures.Boots(),
                [ItemKind.Map] = Textures.Map(),
                [ItemKind.Bomb] = Textures.Bomb(),
            };

            _font = new Font();
            _hud = new Hud(_font);
            _audio = new Audio(_audioOut);
            if (!_capture) { _wallet = SaveSystem.LoadWallet(); _stats = SaveSystem.LoadStats(); }

            EnterMenu();
            if (_capture)
            {
                Directory.CreateDirectory(_capDir);
                if (_iconPath != null) SetupIconScene();
                else if (!TouchUI && !_appStore && !_preview) SetupShot(0); // the others are scripted per frame
            }
        }

        // ---- menu / lifecycle ----------------------------------------------
        void EnterMenu()
        {
            SaveGame();                 // capture current run (no-op if none / dead / creating a hero)
            RecordStats();
            _inCreate = false; _kbOpen = false; // BACK from character creation lands here too
            _menuSave = _capture ? _capSave : SaveSystem.Load();
            _menuItems.Clear();
            if (_menuSave != null) _menuItems.Add(MenuAction.Continue);
            _menuItems.Add(MenuAction.NewGame);
            if (Store != null) _menuItems.Add(MenuAction.Shop);
            if (Leaderboards != null) _menuItems.Add(MenuAction.Leaderboards);
            if (!TouchUI) _menuItems.Add(MenuAction.Quit); // mobile apps don't quit themselves
            _menuSel = 0;
            _inMenu = true;
        }

        void ActivateMenu(MenuAction a)
        {
            switch (a)
            {
                case MenuAction.Continue: ContinueGame(); break;
                case MenuAction.NewGame: EnterCreate(); break;
                case MenuAction.Shop: OpenShop(); break;
                case MenuAction.Leaderboards: OpenBoards(); break;
                case MenuAction.Quit: Close(); break;
            }
        }

        // ---- character creation --------------------------------------------
        void EnterCreate()
        {
            _inMenu = false; _inCreate = true; _dead = false;
            _createSel = 0; _kbOpen = false;
            RollStats();
        }

        // ---- point-buy attribute access ------------------------------------
        int GetAttr(int i) => i == 0 ? _str : i == 1 ? _dex : i == 2 ? _vit : _luk;
        void SetAttr(int i, int v) { switch (i) { case 0: _str = v; break; case 1: _dex = v; break; case 2: _vit = v; break; default: _luk = v; break; } }
        int PointsLeft() => PbTotal - (_str + _dex + _vit + _luk);

        void IncAttr(int i)
        {
            if (PointsLeft() > 0 && GetAttr(i) < PbMax) { SetAttr(i, GetAttr(i) + 1); _audio.Play(Sound.Step, 0.3f, 0.2f); }
        }
        void DecAttr(int i)
        {
            if (GetAttr(i) > PbMin) { SetAttr(i, GetAttr(i) - 1); _audio.Play(Sound.Step, 0.3f, 0.2f); }
        }

        // Randomly distribute the point-buy budget across the four attributes.
        void RollStats()
        {
            var a = new[] { PbMin, PbMin, PbMin, PbMin };
            int pts = PbTotal - PbMin * 4;
            while (pts > 0)
            {
                int i = _rng.Next(4);
                if (a[i] < PbMax) { a[i]++; pts--; }
            }
            _str = a[0]; _dex = a[1]; _vit = a[2]; _luk = a[3];
            _audio.Play(Sound.Gold, 0.4f, 0.15f); // little "dice" chime
        }

        // Class is auto-assigned from the dominant attribute (ties: STR>DEX>VIT>LUK).
        static HeroClass ClassOf(int str, int dex, int vit, int luk)
        {
            int mx = Math.Max(Math.Max(str, dex), Math.Max(vit, luk));
            if (str == mx) return HeroClass.Warrior;
            if (dex == mx) return HeroClass.Rogue;
            if (vit == mx) return HeroClass.Guardian;
            return HeroClass.Trickster;
        }

        // Pure derivation of the numbers combat uses, including the class passive.
        static (int maxHp, int atkMin, int atkMax, int dodge, int crit, HeroClass cls)
            Derive(int str, int dex, int vit, int luk)
        {
            int maxHp = 12 + vit;
            int atkMin = 1 + str / 4;
            int atkMax = atkMin + 2 + str / 6;
            int dodge = Math.Clamp(dex * 2, 0, 40);
            int crit = Math.Clamp(luk * 2, 0, 40);
            var cls = ClassOf(str, dex, vit, luk);
            switch (cls) // class passives
            {
                case HeroClass.Warrior: atkMin++; atkMax++; break;
                case HeroClass.Rogue: dodge = Math.Clamp(dodge + 5, 0, 45); break;
                case HeroClass.Guardian: maxHp += 4; break;
                case HeroClass.Trickster: crit = Math.Clamp(crit + 5, 0, 45); break;
            }
            return (maxHp, atkMin, atkMax, dodge, crit, cls);
        }

        void ApplyStats()
        {
            var d = Derive(_str, _dex, _vit, _luk);
            _maxHp = d.maxHp; _hp = d.maxHp;
            _atkMin = d.atkMin; _atkMax = d.atkMax;
            _dodgePct = d.dodge; _critPct = d.crit;
            _className = d.cls.ToString().ToUpperInvariant();
        }

        void BeginRun()
        {
            _inCreate = false; _dead = false;
            if (string.IsNullOrWhiteSpace(_name)) _name = "HERO";
            _depth = 1; _gold = 0; _shield = 0;
            _torchTurns = _atkTurns = _atkBonus = _regenTurns = _freeze = _haste = 0; _turnNo = 0;
            ApplyStats();
            RebuildDepthTextures(_depth);
            BuildLevel();
            _intro = 7f;
            Log($"{_name} THE {_className} ENTERS >", new Vector4(1f, 0.85f, 0.5f, 1f));
        }

        void ContinueGame()
        {
            var d = _menuSave ?? SaveSystem.Load();
            if (d == null) { EnterCreate(); return; }

            _depth = d.Depth; _hp = d.Hp; _maxHp = d.MaxHp;
            _atkMin = d.AtkMin; _atkMax = d.AtkMax; _gold = d.Gold; _shield = d.Shield;
            _torchTurns = d.TorchTurns; _atkTurns = d.AtkTurns; _atkBonus = d.AtkBonus;
            _regenTurns = d.RegenTurns; _freeze = d.Freeze; _haste = d.Haste;
            _name = string.IsNullOrEmpty(d.Name) ? "HERO" : d.Name;
            _str = d.Str; _dex = d.Dex; _vit = d.Vit; _luk = d.Luk;
            _dodgePct = d.DodgePct; _critPct = d.CritPct; _className = d.ClassName;
            if (string.IsNullOrEmpty(_className)) // migrate a pre-class save
            {
                var dv = Derive(_str, _dex, _vit, _luk);
                _dodgePct = dv.dodge; _critPct = dv.crit; _className = dv.cls.ToString().ToUpperInvariant();
            }
            RebuildDepthTextures(_depth);

            _level = new Level(d.W, d.H, d.Tiles, d.StartX, d.StartY, d.ExitX, d.ExitY);
            _seen = new bool[d.W, d.H];
            for (int x = 0; x < d.W; x++)
                for (int y = 0; y < d.H; y++)
                    _seen[x, y] = d.Seen[y * d.W + x];

            _px = d.Px; _py = d.Py; _dir = d.Dir;
            _rcx = _px + 0.5f; _rcz = _py + 0.5f;
            _ryaw = _targetYaw = _dir * MathF.PI / 2f;
            _anim = false; _pendingDescend = false;

            _enemies.Clear();
            foreach (var e in d.Enemies)
                _enemies.Add(new Enemy
                {
                    X = e.X, Y = e.Y, Rx = e.X, Ry = e.Y, Kind = (EnemyKind)e.Kind, Name = e.Name,
                    Hp = e.Hp, MaxHp = e.MaxHp, Dmg = e.Dmg, MoveEvery = e.MoveEvery, MoveCounter = e.MoveCounter,
                });
            _items.Clear();
            foreach (var it in d.Items)
                _items.Add(new Item { X = it.X, Y = it.Y, Kind = (ItemKind)it.Kind, Value = it.Value });

            RebuildGeometry();

            _inMenu = false; _inCreate = false; _dead = false; _intro = 0f;
            StartMusic();
            Log($"WELCOME BACK - DEPTH {_depth}", new Vector4(1f, 0.85f, 0.5f, 1f));
        }

        // Kick off background generation of this depth's music track.
        void StartMusic()
        {
            int depth = _depth;
            _musicTask = Task.Run(() => MusicGen.Generate(depth));
        }

        void SaveGame()
        {
            if (_capture) return; // capture mode must never touch the player's real save
            // Character creation edits the hero fields in place, over the paused run, so saving
            // then would stamp the half-made hero onto that run.
            if (_inMenu || _inCreate || _dead || _level == null) return;
            SaveSystem.Save(Snapshot());
        }

        // The whole run as a SaveData.
        SaveData Snapshot()
        {
            var d = new SaveData
            {
                Depth = _depth, Hp = _hp, MaxHp = _maxHp, AtkMin = _atkMin, AtkMax = _atkMax, Gold = _gold, Shield = _shield,
                TorchTurns = _torchTurns, AtkTurns = _atkTurns, AtkBonus = _atkBonus,
                RegenTurns = _regenTurns, Freeze = _freeze, Haste = _haste,
                Name = _name, Str = _str, Dex = _dex, Vit = _vit, Luk = _luk,
                DodgePct = _dodgePct, CritPct = _critPct, ClassName = _className,
                Px = _px, Py = _py, Dir = _dir,
                W = _level.W, H = _level.H, Tiles = _level.FlatTiles(),
                StartX = _level.Start.x, StartY = _level.Start.y, ExitX = _level.ExitPos.x, ExitY = _level.ExitPos.y,
                Seen = FlattenSeen(),
            };
            foreach (var e in _enemies)
                d.Enemies.Add(new EnemyDto
                {
                    X = e.X, Y = e.Y, Kind = (int)e.Kind, Hp = e.Hp, MaxHp = e.MaxHp,
                    Dmg = e.Dmg, MoveEvery = e.MoveEvery, MoveCounter = e.MoveCounter, Name = e.Name,
                });
            foreach (var it in _items)
                d.Items.Add(new ItemDto { X = it.X, Y = it.Y, Kind = (int)it.Kind, Value = it.Value });
            return d;
        }

        bool[] FlattenSeen()
        {
            var s = new bool[_level.W * _level.H];
            for (int x = 0; x < _level.W; x++)
                for (int y = 0; y < _level.H; y++)
                    s[y * _level.W + x] = _seen[x, y];
            return s;
        }

        void RebuildDepthTextures(int depth)
        {
            _wallTex.Dispose(); _floorTex.Dispose(); _ceilTex.Dispose();
            _wallNrm.Dispose(); _floorNrm.Dispose(); _ceilNrm.Dispose();
            var wall = Textures.StoneWall(depth);
            var floor = Textures.Floor(depth);
            var ceil = Textures.Ceiling(depth);
            _wallTex = wall.Albedo; _wallNrm = wall.Normal;
            _floorTex = floor.Albedo; _floorNrm = floor.Normal;
            _ceilTex = ceil.Albedo; _ceilNrm = ceil.Normal;
        }

        // ---- level setup ----------------------------------------------------
        // Bake the level's walls, floor and ceiling into three static meshes.
        void RebuildGeometry()
        {
            var walls = new List<Vector3>();
            var floors = new List<Vector3>();
            var ceils = new List<Vector3>();
            _exits.Clear();
            for (int x = 0; x < _level.W; x++)
                for (int y = 0; y < _level.H; y++)
                {
                    var t = _level.At(x, y);
                    if (t == Tile.Wall) { if (TouchesOpen(x, y)) walls.Add(new Vector3(x, 0, y)); continue; }
                    if (t == Tile.Exit) _exits.Add((x, y)); // drawn separately with the portal texture
                    else floors.Add(new Vector3(x, 0, y));
                    if (_level.IsWalkable(x, y)) ceils.Add(new Vector3(x, 1, y));
                }
            _wallsMesh?.Dispose(); _floorMesh?.Dispose(); _ceilMesh?.Dispose();
            _wallsMesh = Mesh.Tiled(Mesh.CubeVerts(), walls);
            _floorMesh = Mesh.Tiled(Mesh.FloorVerts(), floors);
            _ceilMesh = Mesh.Tiled(Mesh.CeilVerts(), ceils);
        }

        bool TouchesOpen(int x, int y)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (_level.IsWalkable(x + dx, y + dy)) return true;
            return false;
        }

        void SpawnEntities()
        {
            var cells = _level.FloorCells();
            Shuffle(cells);
            int enemyCount = Math.Min(4 + _depth * 2, 22);
            int idx = 0;

            foreach (var (x, y) in cells)
            {
                if (idx >= enemyCount) break;
                if (Manhattan(x, y, _px, _py) < 6) continue; // breathing room near start
                if (EnemyAt(x, y) != null) continue;
                _enemies.Add(MakeEnemy(x, y));
                idx++;
            }

            // items scattered on remaining cells
            int potions = 3 + _depth / 2, golds = 5 + _depth, swords = _rng.Next(0, 2), hearts = (_depth % 3 == 0) ? 1 : 0;
            int shields = _rng.Next(0, 3) == 0 ? 1 : 0;   // ~1-in-3 floors carry a shield
            void Place(ItemKind k, int val)
            {
                for (int tries = 0; tries < 40; tries++)
                {
                    var c = cells[_rng.Next(cells.Count)];
                    if (Manhattan(c.x, c.y, _px, _py) < 3) continue;
                    if (ItemAt(c.x, c.y) != null || _level.At(c.x, c.y) == Tile.Exit) continue;
                    _items.Add(new Item { X = c.x, Y = c.y, Kind = k, Value = val });
                    return;
                }
            }
            for (int i = 0; i < potions; i++) Place(ItemKind.Potion, 8 + _depth);
            for (int i = 0; i < golds; i++) Place(ItemKind.Gold, _rng.Next(5, 15) + _depth * 2);
            for (int i = 0; i < swords; i++) Place(ItemKind.Sword, 1);
            for (int i = 0; i < hearts; i++) Place(ItemKind.Heart, 4);
            for (int i = 0; i < shields; i++) Place(ItemKind.Shield, 5);   // halves the next 5 hits

            // extra consumables & trinkets, each fairly uncommon
            if (_rng.Next(3) == 0) Place(ItemKind.Oil, 80);                // brighter torch for 80 turns
            if (_rng.Next(5) == 0) Place(ItemKind.Ring, 0);                // permanent +4% dodge & crit
            if (_rng.Next(4) == 0) Place(ItemKind.Gem, 40 + _depth * 5);   // fat gold hit
            if (_rng.Next(3) == 0) Place(ItemKind.Whetstone, 45);          // +3 atk for 45 turns
            for (int i = 0; i < _rng.Next(1, 3); i++) Place(ItemKind.Bread, 5); // cheap flat heals
            if (_rng.Next(4) == 0) Place(ItemKind.Regen, 20);              // 1 HP/turn for 20 turns
            if (_rng.Next(4) == 0) Place(ItemKind.Hourglass, 12);          // freeze enemies 12 turns
            if (_rng.Next(4) == 0) Place(ItemKind.Boots, 20);              // enemies half-speed 20 turns
            if (_rng.Next(3) == 0) Place(ItemKind.Map, 0);                 // reveal the floor
            if (_rng.Next(3) == 0) Place(ItemKind.Bomb, 0);                // fiery area blast on pickup
        }

        Enemy MakeEnemy(int x, int y)
        {
            // weight kinds by depth: shallow = rats/bats, deep = slimes/skeletons
            EnemyKind kind;
            int roll = _rng.Next(100);
            if (_depth <= 2) kind = roll < 60 ? EnemyKind.Rat : EnemyKind.Bat;
            else if (_depth <= 4) kind = roll < 35 ? EnemyKind.Rat : roll < 60 ? EnemyKind.Bat : roll < 85 ? EnemyKind.Slime : EnemyKind.Skeleton;
            else kind = roll < 20 ? EnemyKind.Bat : roll < 55 ? EnemyKind.Slime : EnemyKind.Skeleton;

            int baseHp, dmg, every; string name;
            switch (kind)
            {
                case EnemyKind.Rat: baseHp = 5; dmg = 2; every = 1; name = "RAT"; break;
                case EnemyKind.Bat: baseHp = 4; dmg = 2; every = 1; name = "BAT"; break;
                case EnemyKind.Slime: baseHp = 11; dmg = 3; every = 2; name = "SLIME"; break;
                default: baseHp = 14; dmg = 4; every = 1; name = "SKELETON"; break;
            }
            int hp = baseHp + _depth * 2;
            return new Enemy
            {
                X = x, Y = y, Rx = x, Ry = y, Kind = kind, Name = name,
                Hp = hp, MaxHp = hp, Dmg = dmg + _depth / 3, MoveEvery = every,
            };
        }

        // =====================================================================
        /// <summary>Advance the simulation by <paramref name="dt"/> seconds.</summary>
        public void Update(float dt)
        {
            _time += dt;
            PumpStore();
            PumpLeaderboards();

            // hand a finished music track to the audio engine (created on this thread)
            if (_musicTask != null && _musicTask.IsCompleted)
            {
                if (_musicTask.IsCompletedSuccessfully) _audio.SetMusic(_musicTask.Result);
                _musicTask = null;
            }

            if (_screenFlash > 0) _screenFlash = Math.Max(0, _screenFlash - dt * 1.6f);
            if (_descendFlash > 0) _descendFlash = Math.Max(0, _descendFlash - dt * 1.2f);
            if (_intro > 0) _intro -= dt;
            if (_levelBanner > 0) _levelBanner -= dt;
            for (int i = _log.Count - 1; i >= 0; i--)
            {
                var m = _log[i]; m.Age += dt; _log[i] = m;
                if (m.Age > 5f) _log.RemoveAt(i);
            }

            // player move/turn tween
            if (_anim)
            {
                _animT += dt / _animDur;
                if (_animT >= 1f)
                {
                    _animT = 1f; _anim = false;
                    _rcx = _toCx; _rcz = _toCz; _ryaw = _toYaw;
                    if (_pendingDescend) { _pendingDescend = false; Descend(); }
                    else RunQueuedSwipe();
                }
                else
                {
                    float t = Smooth(_animT);
                    _rcx = Lerp(_fromCx, _toCx, t);
                    _rcz = Lerp(_fromCz, _toCz, t);
                    _ryaw = Lerp(_fromYaw, _toYaw, t);
                }
            }

            // enemy render glide + flash decay
            foreach (var e in _enemies)
            {
                e.Rx = Approach(e.Rx, e.X, dt * 6f);
                e.Ry = Approach(e.Ry, e.Y, dt * 6f);
                if (e.Flash > 0) e.Flash = Math.Max(0, e.Flash - dt * 4f);
            }
        }

        // ---- input ----------------------------------------------------------
        public void KeyDown(Key key)
        {
            // M toggles music — but not while naming a hero (so names may contain 'M').
            if (key == Key.M && !_inCreate)
            {
                ToggleMusic();
                return;
            }

            if (_inShop) { HandleShopKey(key); return; }
            if (_inBoards) { HandleBoardsKey(key); return; }
            if (_inMenu) { HandleMenuKey(key); return; }
            if (_inCreate) { HandleCreateKey(key); return; }

            if (key == Key.Escape) { EnterMenu(); return; } // pause to menu (auto-saves)

            if (_dead)
            {
                if (key == Key.C && Spendable() >= ReviveCost()) Revive();
                else if (key == Key.Enter || key == Key.Space) EnterCreate();
                return;
            }

            if (_anim || _pendingDescend) return;

            switch (key)
            {
                case Key.W: case Key.Up: Step(_dir, attack: true); break;
                case Key.S: case Key.Down: Step((_dir + 2) % 4, attack: false); break;
                case Key.Q: Step((_dir + 3) % 4, attack: false); break;
                case Key.E: Step((_dir + 1) % 4, attack: false); break;
                case Key.A: case Key.Left: Turn(-1); break;
                case Key.D: case Key.Right: Turn(+1); break;
                case Key.Space: case Key.Enter: case Key.F: AttackFront(); break;
            }
        }

        void ToggleMusic()
        {
            bool on = _audio.ToggleMusic();
            Log(on ? "MUSIC ON" : "MUSIC OFF", new Vector4(0.7f, 0.85f, 1f, 1f));
        }

        void HandleMenuKey(Key k)
        {
            int n = _menuItems.Count;
            switch (k)
            {
                case Key.Up: case Key.W: _menuSel = (_menuSel - 1 + n) % n; break;
                case Key.Down: case Key.S: _menuSel = (_menuSel + 1) % n; break;
                case Key.Enter: case Key.Space: ActivateMenu(_menuItems[_menuSel]); break;
                case Key.Escape: if (!TouchUI) Close(); break;
            }
        }

        void HandleCreateKey(Key k)
        {
            switch (k)
            {
                case Key.Up: _createSel = (_createSel + 3) % 4; break;
                case Key.Down: _createSel = (_createSel + 1) % 4; break;
                case Key.Left: DecAttr(_createSel); break;
                case Key.Right: IncAttr(_createSel); break;
                case Key.Backspace: NameBackspace(); break;
                case Key.Enter: BeginRun(); break;
                case Key.Tab: RollStats(); break;
                case Key.Escape: EnterMenu(); break;
            }
        }

        // Character-creation text entry (the font is uppercase-only).
        public void TextInput(string text)
        {
            if (!_inCreate) return;
            foreach (char c in text) NameAppend(c);
        }

        const int NameMax = 14;
        void NameAppend(char c)
        {
            if (_name.Length >= NameMax) return;
            if (char.IsAsciiLetterOrDigit(c) || c == ' ') _name += char.ToUpperInvariant(c);
        }
        void NameBackspace() { if (_name.Length > 0) _name = _name[..^1]; }

        void ActivateCreate(CreateAction a)
        {
            switch (a)
            {
                case CreateAction.Reroll: RollStats(); break;
                case CreateAction.Begin: BeginRun(); break;
                case CreateAction.Back: EnterMenu(); break;
            }
        }

        // A primary press (left click / touch down), in framebuffer pixels.
        public void PointerDown(float mx, float my)
        {
            _ptrX = mx; _ptrY = my; _ptrHeld = true;
            if (_inShop) { ShopPointerDown(mx, my); return; }
            if (_inBoards) { BoardsPointerDown(mx, my); return; }
            if (TouchUI && TouchPointerDown(mx, my)) return;

            if (_inMenu)
            {
                var btns = MenuButtons();
                for (int i = 0; i < btns.Count; i++)
                {
                    var b = btns[i];
                    if (mx >= b.x && mx <= b.x + b.w && my >= b.y && my <= b.y + b.h)
                    { _menuSel = i; ActivateMenu(b.action); return; }
                }
            }
            else if (_inCreate)
            {
                // fingers need more room than the drawn steppers: pad the hit box into the gaps
                float ui = FramebufferSize.Y / 720f;
                float px = TouchUI ? 14 * ui : 0, py = TouchUI ? 7 * ui : 0;
                foreach (var b in AttrButtons())
                    if (Inside(mx, my, b.x - px, b.y - py, b.w + 2 * px, b.h + 2 * py))
                    {
                        _createSel = b.attr;
                        if (b.delta > 0) IncAttr(b.attr); else DecAttr(b.attr);
                        return;
                    }
                foreach (var b in CreateButtons())
                    if (mx >= b.x && mx <= b.x + b.w && my >= b.y && my <= b.y + b.h)
                    { ActivateCreate(b.action); return; }
            }
        }

        public void PointerMove(float mx, float my)
        {
            _ptrX = mx; _ptrY = my;
            if (TouchUI && _ptrHeld) TouchPointerMove(mx, my);
        }

        public void PointerUp(float mx, float my)
        {
            _ptrHeld = false;
            if (TouchUI)
            {
                TouchPointerUp(mx, my);
                _ptrX = _ptrY = -1e6f; // a lifted finger hovers nothing
            }
        }

        // Last known pointer position (framebuffer pixels) for hover highlights.
        float _ptrX = -1e6f, _ptrY = -1e6f;
        bool _ptrHeld;
        (float x, float y) MouseFb() => (_ptrX, _ptrY);

        List<(MenuAction action, string label, float x, float y, float w, float h)> MenuButtons()
        {
            int W = FramebufferSize.X, H = FramebufferSize.Y;
            float ui = H / 720f;
            int n = _menuItems.Count;
            float bw = 380 * ui, bh = (n > 3 ? 54 : 62) * ui, gap = (n > 3 ? 14 : 22) * ui;
            float total = n * bh + (n - 1) * gap;
            float startY = H * 0.52f - total / 2f;
            float x = W / 2f - bw / 2f;
            var list = new List<(MenuAction, string, float, float, float, float)>();
            for (int i = 0; i < n; i++)
            {
                var a = _menuItems[i];
                string label = a switch
                {
                    MenuAction.Continue => _menuSave != null ? $"CONTINUE - DEPTH {_menuSave.Depth}" : "CONTINUE",
                    MenuAction.NewGame => "NEW GAME",
                    MenuAction.Shop => "BUY GOLD",
                    MenuAction.Leaderboards => "LEADERBOARDS",
                    _ => "QUIT",
                };
                list.Add((a, label, x, startY + i * (bh + gap), bw, bh));
            }
            return list;
        }

        List<(CreateAction action, string label, float x, float y, float w, float h)> CreateButtons()
        {
            int W = FramebufferSize.X, H = FramebufferSize.Y;
            float ui = H / 720f;
            float bw = 220 * ui, bh = 56 * ui, gap = 28 * ui;
            var labels = new[] { (CreateAction.Reroll, "REROLL"), (CreateAction.Begin, "BEGIN"), (CreateAction.Back, "BACK") };
            float total = labels.Length * bw + (labels.Length - 1) * gap;
            float x0 = W / 2f - total / 2f;
            float y = H * 0.8f;
            var list = new List<(CreateAction, string, float, float, float, float)>();
            for (int i = 0; i < labels.Length; i++)
                list.Add((labels[i].Item1, labels[i].Item2, x0 + i * (bw + gap), y, bw, bh));
            return list;
        }

        // Shared layout for one point-buy attribute row (keeps render + hit-test in sync).
        (float labelX, float minusX, float valueX, float plusX, float derivedX, float y, float bw, float bh) AttrRow(int i)
        {
            int W = FramebufferSize.X, H = FramebufferSize.Y;
            float ui = H / 720f, cx = W / 2f;
            float y = H * 0.37f + i * 48 * ui;
            return (cx - 330 * ui, cx - 80 * ui, cx - 22 * ui, cx + 34 * ui, cx + 120 * ui, y, 38 * ui, 34 * ui);
        }

        List<(int attr, int delta, float x, float y, float w, float h)> AttrButtons()
        {
            float ui = FramebufferSize.Y / 720f;
            var list = new List<(int, int, float, float, float, float)>();
            for (int i = 0; i < 4; i++)
            {
                var r = AttrRow(i);
                float by = r.y - 3 * ui;
                list.Add((i, -1, r.minusX, by, r.bw, r.bh));
                list.Add((i, +1, r.plusX, by, r.bw, r.bh));
            }
            return list;
        }

        void Turn(int sign)
        {
            _dir = (_dir + sign + 4) % 4;
            _fromCx = _toCx = _rcx; _fromCz = _toCz = _rcz;
            _fromYaw = _ryaw; _targetYaw += sign * MathF.PI / 2f; _toYaw = _targetYaw;
            _animDur = 0.12f; _animT = 0f; _anim = true;
        }

        void Step(int dir, bool attack)
        {
            int tx = _px + DX[dir], ty = _py + DY[dir];
            var target = EnemyAt(tx, ty);
            if (target != null)
            {
                if (attack) { _audio.Play(Sound.Swing, 0.6f, 0.1f); HitEnemy(target); AdvanceTurn(); }
                return; // don't walk into monsters
            }
            if (!_level.IsWalkable(tx, ty)) { _audio.Play(Sound.Bump, 0.6f, 0.1f); return; } // wall

            _audio.Play(Sound.Step, 0.55f, 0.12f);
            _fromCx = _rcx; _fromCz = _rcz; _toCx = tx + 0.5f; _toCz = ty + 0.5f;
            _fromYaw = _toYaw = _ryaw;
            _animDur = 0.11f; _animT = 0f; _anim = true;

            _px = tx; _py = ty;
            CollectAt(_px, _py);
            MarkSeen();
            if (_level.At(_px, _py) == Tile.Exit) _pendingDescend = true;
            AdvanceTurn();
        }

        void AttackFront()
        {
            int tx = _px + DX[_dir], ty = _py + DY[_dir];
            _audio.Play(Sound.Swing, 0.6f, 0.1f);
            var target = EnemyAt(tx, ty);
            if (target != null) { HitEnemy(target); AdvanceTurn(); }
        }

        // ---- combat ---------------------------------------------------------
        void HitEnemy(Enemy e)
        {
            int dmg = _rng.Next(_atkMin + _atkBonus, _atkMax + _atkBonus + 1);
            bool crit = _rng.Next(100) < _critPct; // LUK
            if (crit) dmg *= 2;
            e.Hp -= dmg; e.Flash = 0.3f;
            _audio.Play(Sound.Hit, 0.8f, 0.12f);
            if (e.Hp <= 0)
            {
                _enemies.Remove(e);
                _stats.Kills++;
                _audio.Play(Sound.Kill, 0.8f, 0.1f);
                Log($"{e.Name} SLAIN!", new Vector4(0.7f, 1f, 0.7f, 1f));
                // loot — luck improves the odds
                int r = _rng.Next(100);
                int goldChance = 35 + _luk, potionChance = goldChance + 15 + _luk / 2;
                if (r < goldChance) _items.Add(new Item { X = e.X, Y = e.Y, Kind = ItemKind.Gold, Value = _rng.Next(4, 12) + _depth });
                else if (r < potionChance) _items.Add(new Item { X = e.X, Y = e.Y, Kind = ItemKind.Potion, Value = 8 + _depth });
            }
            else if (crit) Log($"CRITICAL! {e.Name} FOR {dmg}", new Vector4(1f, 0.7f, 0.3f, 1f));
            else Log($"HIT {e.Name} FOR {dmg}", new Vector4(1f, 0.9f, 0.6f, 1f));
        }

        // A fire scroll / bomb: scorch every enemy within `radius` tiles for `dmg`.
        void FireBlast(int cx, int cy, int radius, int dmg)
        {
            _screenFlash = 0.7f;
            _descendFlash = 0.5f;
            _audio.Play(Sound.Kill, 0.9f);
            Log("THE SCROLL ERUPTS IN FLAME!", new Vector4(1f, 0.55f, 0.2f, 1f));
            var caught = new List<Enemy>();
            foreach (var e in _enemies)
                if (Manhattan(e.X, e.Y, cx, cy) <= radius) caught.Add(e);
            foreach (var e in caught)
            {
                e.Hp -= dmg; e.Flash = 0.3f;
                if (e.Hp <= 0)
                {
                    _enemies.Remove(e);
                    _stats.Kills++;
                    Log($"{e.Name} IS INCINERATED", new Vector4(1f, 0.7f, 0.4f, 1f));
                }
            }
        }

        void AdvanceTurn()
        {
            _turnNo++;
            if (_regenTurns > 0 && _hp < _maxHp) _hp = Math.Min(_maxHp, _hp + 1);

            // hourglass fully freezes enemies; boots let you act twice per enemy turn
            bool enemiesAct = _freeze == 0 && !(_haste > 0 && (_turnNo % 2 == 0));
            if (enemiesAct) AdvanceEnemies();

            TickBuffs();
        }

        void TickBuffs()
        {
            if (_torchTurns > 0 && --_torchTurns == 0) Log("THE TORCH DIMS AGAIN", new Vector4(0.8f, 0.7f, 0.5f, 1f));
            if (_atkTurns > 0 && --_atkTurns == 0) { _atkBonus = 0; Log("YOUR EDGE DULLS", new Vector4(0.8f, 0.8f, 0.85f, 1f)); }
            if (_regenTurns > 0) --_regenTurns;
            if (_freeze > 0 && --_freeze == 0) Log("THE ENEMIES STIR", new Vector4(0.8f, 0.7f, 0.7f, 1f));
            if (_haste > 0 && --_haste == 0) Log("YOUR HASTE FADES", new Vector4(0.8f, 0.85f, 0.85f, 1f));
        }

        void AdvanceEnemies()
        {
            var occupied = new HashSet<(int, int)>();
            foreach (var en in _enemies) occupied.Add((en.X, en.Y));

            foreach (var e in _enemies)
            {
                int man = Manhattan(e.X, e.Y, _px, _py);
                if (++e.MoveCounter < e.MoveEvery) continue;
                e.MoveCounter = 0;

                if (man == 1) { PlayerTakeDamage(e); continue; }
                if (man > 9) continue; // out of sight, idle

                // greedy chase, bats wander a little
                int wantx = Math.Sign(_px - e.X), wanty = Math.Sign(_py - e.Y);
                bool wander = e.Kind == EnemyKind.Bat && _rng.Next(100) < 35;
                var tries = new List<(int, int)>();
                if (wander)
                {
                    tries.Add((e.X + (_rng.Next(3) - 1), e.Y));
                    tries.Add((e.X, e.Y + (_rng.Next(3) - 1)));
                }
                if (Math.Abs(_px - e.X) >= Math.Abs(_py - e.Y))
                { tries.Add((e.X + wantx, e.Y)); tries.Add((e.X, e.Y + wanty)); }
                else
                { tries.Add((e.X, e.Y + wanty)); tries.Add((e.X + wantx, e.Y)); }

                foreach (var (nx, ny) in tries)
                {
                    if (nx == e.X && ny == e.Y) continue;
                    if (!_level.IsWalkable(nx, ny)) continue;
                    if (nx == _px && ny == _py) continue;       // that's the player
                    if (occupied.Contains((nx, ny))) continue;
                    occupied.Remove((e.X, e.Y));
                    e.X = nx; e.Y = ny;
                    occupied.Add((nx, ny));
                    break;
                }
            }
        }

        void PlayerTakeDamage(Enemy e)
        {
            if (_rng.Next(100) < _dodgePct) // DEX
            {
                _audio.Play(Sound.Swing, 0.4f, 0.15f);
                Log($"YOU DODGE THE {e.Name}", new Vector4(0.7f, 0.9f, 1f, 1f));
                return;
            }
            int dmg = e.Dmg;
            if (_shield > 0)
            {
                dmg = (dmg + 1) / 2;                 // halve, rounded up
                _shield--;
                Log($"{e.Name} HITS YOU FOR {dmg} (SHIELDED)", new Vector4(0.7f, 0.85f, 1f, 1f));
                if (_shield == 0) Log("YOUR SHIELD SHATTERS", new Vector4(0.6f, 0.7f, 0.9f, 1f));
            }
            else
            {
                Log($"{e.Name} HITS YOU FOR {dmg}", new Vector4(1f, 0.5f, 0.5f, 1f));
            }
            _hp -= dmg;
            _screenFlash = 0.6f;
            _audio.Play(Sound.Hurt, 0.7f, 0.08f);
            if (_hp <= 0)
            {
                _hp = 0; _dead = true;
                _stats.Deaths++;
                RecordStats();
                _audio.Play(Sound.Death, 0.9f);
                SaveSystem.Delete(); // dead: only a paid revive brings you back
                Log($"{_name} HAS FALLEN", new Vector4(1f, 0.3f, 0.3f, 1f));
            }
        }

        int ReviveCost() => 30 + _depth * 20;

        // Spend gold to get back up — right where you fell, at full health.
        void Revive()
        {
            int cost = ReviveCost();
            if (Spendable() < cost) return;
            bool fromWallet = ApplyWallet(); // purchases made on the death screen
            _gold -= cost;
            _dead = false;
            _hp = _maxHp;
            // stay put: snap the render camera to the current grid pos/facing
            _rcx = _px + 0.5f; _rcz = _py + 0.5f; _ryaw = _targetYaw = _dir * MathF.PI / 2f;
            _anim = false; _pendingDescend = false; _screenFlash = 0f;
            MarkSeen();
            _audio.Play(Sound.Power, 0.9f);
            Log($"{_name} IS REVIVED!  -{cost} GOLD", new Vector4(0.7f, 1f, 0.8f, 1f));
            SaveGame(); // re-persist the living run
            if (fromWallet) CommitWallet();
        }

        // ---- items ----------------------------------------------------------
        void CollectAt(int x, int y)
        {
            var it = ItemAt(x, y);
            if (it == null) return;
            _items.Remove(it);
            switch (it.Kind)
            {
                case ItemKind.Potion:
                    _hp = Math.Min(_maxHp, _hp + it.Value);
                    _audio.Play(Sound.Potion, 0.8f);
                    Log($"POTION +{it.Value} HP", new Vector4(0.6f, 1f, 0.7f, 1f)); break;
                case ItemKind.Gold:
                    _gold += it.Value; _stats.Gold += it.Value;
                    _audio.Play(Sound.Gold, 0.7f, 0.06f);
                    Log($"+{it.Value} GOLD", new Vector4(1f, 0.9f, 0.4f, 1f)); break;
                case ItemKind.Sword:
                    _atkMin++; _atkMax++;
                    _audio.Play(Sound.Power, 0.8f);
                    Log("SHARPER BLADE! +1 ATK", new Vector4(0.8f, 0.9f, 1f, 1f)); break;
                case ItemKind.Heart:
                    _maxHp += it.Value; _hp += it.Value;
                    _audio.Play(Sound.Power, 0.8f);
                    Log($"MAX HP +{it.Value}", new Vector4(1f, 0.6f, 0.7f, 1f)); break;
                case ItemKind.Shield:
                    _shield += it.Value;
                    _audio.Play(Sound.Power, 0.8f);
                    Log($"SHIELD RAISED - HALVES {_shield} HITS", new Vector4(0.7f, 0.85f, 1f, 1f)); break;
                case ItemKind.Oil:
                    _torchTurns += it.Value;
                    _audio.Play(Sound.Power, 0.8f);
                    Log("TORCH OIL - THE LIGHT BLAZES", new Vector4(1f, 0.85f, 0.4f, 1f)); break;
                case ItemKind.Ring:
                    _dodgePct = Math.Min(90, _dodgePct + 4); _critPct = Math.Min(90, _critPct + 4);
                    _audio.Play(Sound.Power, 0.8f);
                    Log("A RING - +4% DODGE & CRIT", new Vector4(0.85f, 0.8f, 1f, 1f)); break;
                case ItemKind.Gem:
                    _gold += it.Value; _stats.Gold += it.Value;
                    _audio.Play(Sound.Gold, 0.8f, 0.05f);
                    Log($"A GLITTERING GEM  +{it.Value} GOLD", new Vector4(0.8f, 0.6f, 1f, 1f)); break;
                case ItemKind.Whetstone:
                    _atkBonus = 3; _atkTurns += it.Value;
                    _audio.Play(Sound.Power, 0.8f);
                    Log("WHETSTONE - +3 ATK FOR A WHILE", new Vector4(0.9f, 0.9f, 1f, 1f)); break;
                case ItemKind.Bread:
                    _hp = Math.Min(_maxHp, _hp + it.Value);
                    _audio.Play(Sound.Potion, 0.7f);
                    Log($"BREAD +{it.Value} HP", new Vector4(0.8f, 0.7f, 0.5f, 1f)); break;
                case ItemKind.Regen:
                    _regenTurns += it.Value;
                    _audio.Play(Sound.Power, 0.8f);
                    Log("HEALING HERB - REGENERATING", new Vector4(0.6f, 1f, 0.7f, 1f)); break;
                case ItemKind.Hourglass:
                    _freeze += it.Value;
                    _audio.Play(Sound.Power, 0.8f);
                    Log($"HOURGLASS - ENEMIES FROZEN ({_freeze})", new Vector4(0.6f, 0.85f, 1f, 1f)); break;
                case ItemKind.Boots:
                    _haste += it.Value;
                    _audio.Play(Sound.Power, 0.8f);
                    Log($"BOOTS OF HASTE - YOU BLUR AHEAD ({_haste})", new Vector4(0.7f, 0.9f, 0.9f, 1f)); break;
                case ItemKind.Map:
                    for (int mx = 0; mx < _level.W; mx++)
                        for (int my = 0; my < _level.H; my++) _seen[mx, my] = true;
                    _audio.Play(Sound.Power, 0.8f);
                    Log("A MAP - THE FLOOR IS REVEALED", new Vector4(0.9f, 0.85f, 0.6f, 1f)); break;
                case ItemKind.Bomb:
                    FireBlast(_px, _py, 3, 6 + _depth); break;
            }
        }

        void Descend()
        {
            _depth++;
            // carry current health to the next floor (no heal on descent)
            _descendFlash = 1f;
            _audio.Play(Sound.Descend, 0.9f);
            RebuildDepthTextures(_depth); // new palette as you go deeper
            BuildLevel();
            Log($"DESCENDING TO DEPTH {_depth}", new Vector4(1f, 0.85f, 0.5f, 1f));
        }

        // Build a fresh level at the current depth, keeping the hero's stats.
        void BuildLevel()
        {
            _level = new Level(32, 32, _depth, _rng);
            _levelBanner = 2.8f; // big centered "ENTERING DEPTH x" announcement
            _seen = new bool[_level.W, _level.H];
            _enemies.Clear(); _items.Clear();
            _px = _level.Start.x; _py = _level.Start.y; _dir = 0;
            _rcx = _px + 0.5f; _rcz = _py + 0.5f; _ryaw = _targetYaw = 0f;
            _anim = false; _pendingDescend = false;
            RebuildGeometry();
            SpawnEntities();
            MarkSeen();
            _stats.Deepest = Math.Max(_stats.Deepest, _depth);
            RecordStats();
            StartMusic();
            SaveGame();
        }

        // =====================================================================
        /// <summary>Draw one frame into the currently bound framebuffer. The host presents it.</summary>
        public void Render()
        {
            GL.Viewport(0, 0, FramebufferSize.X, FramebufferSize.Y);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            if (_level != null)
            {
                float aspect = FramebufferSize.X / (float)Math.Max(1, FramebufferSize.Y);
                var proj = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(72f), aspect, 0.03f, 60f);
                var eye = new Vector3(_rcx, 0.5f, _rcz);
                var fwd = new Vector3(MathF.Sin(_ryaw), 0f, -MathF.Cos(_ryaw));
                var view = Matrix4.LookAt(eye, eye + fwd, Vector3.UnitY);
                float torch = 1f + 0.10f * MathF.Sin(_time * 11f) + 0.06f * MathF.Sin(_time * 27f);
                if (_torchTurns > 0) torch *= 1.7f; // torch oil burns brighter

                RenderWorld(view, proj, eye, torch);
                RenderSprites(view, proj, eye, torch);
            }

            if (_iconPath != null) { IconTick(); return; } // the world only, no HUD

            // the shop and leaderboards replace the screen they were opened from
            if (_inShop) RenderShop();
            else if (_inBoards) RenderBoards();
            else if (_inMenu) RenderMenu();
            else if (_inCreate) RenderCreate();
            else RenderHud();

            if (_capture) CaptureTick();
        }

        // ---- app icon -------------------------------------------------------
        // A skeleton looming in a torchlit corridor, rendered by the game itself.
        void SetupIconScene()
        {
            SetupCaptureDungeon(5, true);
            _log.Clear();
            _enemies.Clear(); _items.Clear();
            int ex = _px + DX[_dir], ey = _py + DY[_dir];
            _enemies.Add(new Enemy { X = ex, Y = ey, Rx = ex, Ry = ey, Kind = EnemyKind.Skeleton, Name = "SKELETON", Hp = 1, MaxHp = 1 });
            // pull the camera back a little so the skeleton fills the frame without clipping
            _rcx -= DX[_dir] * 0.35f; _rcz -= DY[_dir] * 0.35f;
        }

        void IconTick()
        {
            if (++_capSettle < 6) return;
            SaveScreenshot(_iconPath);
            _iconPath = null; _capture = false;
            Close();
        }

        // ---- capture driver -------------------------------------------------
        void CaptureTick()
        {
            if (_preview) { PreviewTick(); return; }
            if (TouchUI || _appStore) { TouchCaptureTick(); return; }
            if (++_capSettle < 6) return; // let a few frames settle before grabbing
            _capSettle = 0;
            string path = Path.Combine(_capDir, $"{_capShot}-{CapShots[_capShot]}.bmp");
            SaveScreenshot(path);
            _capShot++;
            if (_capShot >= CapShots.Length) { Close(); return; }
            SetupShot(_capShot);
        }

        void SetupShot(int i)
        {
            switch (i)
            {
                case 0: EnterMenu(); break;              // title / menu
                case 1: EnterCreate(); break;            // character creation
                case 2: SetupCaptureDungeon(2, false); break;  // exploring the upper floors
                case 3: SetupCaptureDungeon(6, true); break;   // the deep, with buffs lit
            }
        }

        // Build a deterministic, well-framed in-game scene (no save/music side effects).
        void SetupCaptureDungeon(int depth, bool buffs)
        {
            _inMenu = false; _inCreate = false; _dead = false; _intro = 0f;
            _name = "ELARA"; _className = "ROGUE";
            _depth = depth; _gold = 180; _maxHp = 30; _hp = buffs ? 24 : 21;
            _atkMin = 3; _atkMax = 6; _dodgePct = 25; _critPct = 15;
            _shield = _torchTurns = _atkTurns = _atkBonus = _regenTurns = _freeze = _haste = 0;

            RebuildDepthTextures(depth);
            _level = new Level(32, 32, depth, _rng);
            _seen = new bool[_level.W, _level.H];

            // find the longest straight walkable run to look down
            int bestLen = -1, bx = _level.Start.x, by = _level.Start.y, bdir = 0;
            for (int x = 0; x < _level.W; x++)
                for (int y = 0; y < _level.H; y++)
                {
                    if (!_level.IsWalkable(x, y)) continue;
                    for (int d = 0; d < 4; d++)
                    {
                        int len = 0;
                        while (len < 9 && _level.IsWalkable(x + DX[d] * (len + 1), y + DY[d] * (len + 1))) len++;
                        if (len > bestLen) { bestLen = len; bx = x; by = y; bdir = d; }
                    }
                }
            _px = bx; _py = by; _dir = bdir;
            _rcx = _px + 0.5f; _rcz = _py + 0.5f; _ryaw = _targetYaw = _dir * MathF.PI / 2f;
            _anim = false; _pendingDescend = false;

            RebuildGeometry();

            // stage a couple of foes and some loot down the corridor
            _enemies.Clear(); _items.Clear();
            void Foe(int ahead, int side, EnemyKind k, string name, int hp, int dmg)
            {
                int ex = _px + DX[_dir] * ahead + DX[(_dir + 1) % 4] * side;
                int ey = _py + DY[_dir] * ahead + DY[(_dir + 1) % 4] * side;
                if (_level.IsWalkable(ex, ey))
                    _enemies.Add(new Enemy { X = ex, Y = ey, Rx = ex, Ry = ey, Kind = k, Name = name, Hp = hp, MaxHp = hp, Dmg = dmg, MoveEvery = 1 });
            }
            void Loot(int ahead, int side, ItemKind k, int val)
            {
                int ix = _px + DX[_dir] * ahead + DX[(_dir + 1) % 4] * side;
                int iy = _py + DY[_dir] * ahead + DY[(_dir + 1) % 4] * side;
                if (_level.IsWalkable(ix, iy)) _items.Add(new Item { X = ix, Y = iy, Kind = k, Value = val });
            }
            if (!buffs)
            {
                Foe(3, 0, EnemyKind.Rat, "RAT", 7, 2);
                Loot(2, 0, ItemKind.Gold, 20);
                Loot(1, 0, ItemKind.Potion, 10);
            }
            else
            {
                Foe(4, 0, EnemyKind.Skeleton, "SKELETON", 22, 6);
                Foe(3, 0, EnemyKind.Slime, "SLIME", 18, 5);
                Loot(2, 0, ItemKind.Shield, 5);
                Loot(1, 0, ItemKind.Gem, 90);
                _shield = 3; _torchTurns = 50; _regenTurns = 15; _atkBonus = 3; _atkTurns = 30;
            }

            MarkSeen();
            Log(buffs ? "THE AIR TURNS COLD..." : "SOMETHING SKITTERS AHEAD", new Vector4(1f, 0.85f, 0.5f, 1f));
        }

        // Read the back buffer and write it out as a 24-bit BMP (no external deps).
        void SaveScreenshot(string path)
        {
            int w = FramebufferSize.X, h = FramebufferSize.Y;
            var rgba = new byte[w * h * 4];
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
            GL.ReadPixels(0, 0, w, h, PixelFormat.Rgba, PixelType.UnsignedByte, rgba);

            int rowPad = (4 - (w * 3) % 4) % 4;
            int imgSize = (w * 3 + rowPad) * h;
            using var fs = new FileStream(path, FileMode.Create);
            using var bw = new BinaryWriter(fs);
            bw.Write((byte)'B'); bw.Write((byte)'M');
            bw.Write(14 + 40 + imgSize);        // file size
            bw.Write(0);                         // reserved
            bw.Write(14 + 40);                   // pixel data offset
            bw.Write(40);                        // DIB header size
            bw.Write(w); bw.Write(h);            // width, height (bottom-up)
            bw.Write((short)1); bw.Write((short)24);
            bw.Write(0); bw.Write(imgSize);
            bw.Write(2835); bw.Write(2835);      // ~72 DPI
            bw.Write(0); bw.Write(0);
            // ReadPixels gives rows bottom-to-top already — matches BMP order.
            var pad = new byte[rowPad];
            for (int y = 0; y < h; y++)
            {
                int row = y * w * 4;
                for (int x = 0; x < w; x++)
                {
                    int p = row + x * 4;
                    bw.Write(rgba[p + 2]); bw.Write(rgba[p + 1]); bw.Write(rgba[p]); // BGR
                }
                if (rowPad > 0) bw.Write(pad);
            }
        }

        void RenderWorld(Matrix4 view, Matrix4 proj, Vector3 eye, float torch)
        {
            _world.Use();
            _world.Set("uView", view);
            _world.Set("uProj", proj);
            _world.Set("uLightPos", eye);
            _world.Set("uTorch", torch);
            _world.Set("uTex", 0);
            _world.Set("uNormal", 1);
            _world.Set("uBump", 1f);

            // walls, floor, ceiling: pre-baked in world space (see RebuildGeometry)
            _world.Set("uModel", Matrix4.Identity);
            _wallTex.Bind(TextureUnit.Texture0);
            _wallNrm.Bind(TextureUnit.Texture1);
            _wallsMesh.Draw();
            _floorTex.Bind(TextureUnit.Texture0);
            _floorNrm.Bind(TextureUnit.Texture1);
            _floorMesh.Draw();
            _ceilTex.Bind(TextureUnit.Texture0);
            _ceilNrm.Bind(TextureUnit.Texture1);
            _ceilMesh.Draw();

            // exit portal tile(s)
            _exitTex.Bind(TextureUnit.Texture0);
            _flatNrm.Bind(TextureUnit.Texture1);   // portal glows flat
            foreach (var (x, y) in _exits)
            {
                _world.Set("uModel", Matrix4.CreateTranslation(x, 0, y));
                _floor.Draw();
            }
        }

        void RenderSprites(Matrix4 view, Matrix4 proj, Vector3 eye, float torch)
        {
            _sprite.Use();
            _sprite.Set("uView", view);
            _sprite.Set("uProj", proj);
            _sprite.Set("uLightPos", eye);
            _sprite.Set("uTorch", torch);
            _sprite.Set("uTex", 0);

            // items (small, bobbing)
            foreach (var it in _items)
            {
                float bob = 0.12f + MathF.Sin(_time * 3f + it.X * 1.3f + it.Y) * 0.05f;
                var model = Matrix4.CreateScale(0.5f) *
                            BillboardRot(it.X + 0.5f, it.Y + 0.5f, eye) *
                            Matrix4.CreateTranslation(it.X + 0.5f, bob, it.Y + 0.5f);
                _sprite.Set("uModel", model);
                _sprite.Set("uTint", new Vector3(1f, 1f, 1f));
                _itemTex[it.Kind].Bind(TextureUnit.Texture0);
                _bill.Draw();
            }

            // enemies
            foreach (var e in _enemies)
            {
                float sx = e.Rx + 0.5f, sz = e.Ry + 0.5f;
                var model = BillboardRot(sx, sz, eye) * Matrix4.CreateTranslation(sx, 0f, sz);
                _sprite.Set("uModel", model);
                var tint = e.Flash > 0 ? new Vector3(1.8f, 0.7f, 0.7f) : new Vector3(1f, 1f, 1f);
                _sprite.Set("uTint", tint);
                _enemyTex[e.Kind].Bind(TextureUnit.Texture0);
                _bill.Draw();
            }
        }

        Matrix4 BillboardRot(float sx, float sz, Vector3 eye)
        {
            float angle = MathF.Atan2(eye.X - sx, eye.Z - sz);
            return Matrix4.CreateRotationY(angle);
        }

        // ---- menu screen ----------------------------------------------------
        void RenderMenu()
        {
            int W = FramebufferSize.X, H = FramebufferSize.Y;
            float ui = H / 720f;
            _hud.Begin(W, H);

            // darken (over the frozen world if a run is in progress, else solid)
            float bgA = _level != null ? 0.72f : 1f;
            _hud.Quad(0, 0, W, H, new Vector4(0.03f, 0.03f, 0.05f, bgA));

            var gold = new Vector4(1f, 0.82f, 0.45f, 1f);
            var dim = new Vector4(0.7f, 0.72f, 0.8f, 1f);
            _hud.TextCentered("TORCHLIGHT DEPTHS", W / 2f, H * 0.2f, 5.2f * ui, gold);
            _hud.TextCentered("A TORCHLIT DUNGEON CRAWLER", W / 2f, H * 0.2f + 64 * ui, 1.6f * ui, dim);

            // mouse hover overrides keyboard selection for the highlight
            var (mx, my) = MouseFb();
            var btns = MenuButtons();
            for (int i = 0; i < btns.Count; i++)
            {
                var hb = btns[i];
                if (mx >= hb.x && mx <= hb.x + hb.w && my >= hb.y && my <= hb.y + hb.h) { _menuSel = i; break; }
            }

            for (int i = 0; i < btns.Count; i++)
            {
                var b = btns[i];
                bool sel = i == _menuSel;
                var fill = sel ? new Vector4(0.48f, 0.30f, 0.11f, 0.95f) : new Vector4(0.12f, 0.12f, 0.16f, 0.9f);
                _hud.Quad(b.x, b.y, b.w, b.h, fill);
                var edge = sel ? gold : new Vector4(0.35f, 0.35f, 0.42f, 1f);
                float t = 3 * ui;
                _hud.Quad(b.x, b.y, b.w, t, edge);
                _hud.Quad(b.x, b.y + b.h - t, b.w, t, edge);
                _hud.Quad(b.x, b.y, t, b.h, edge);
                _hud.Quad(b.x + b.w - t, b.y, t, b.h, edge);

                var txtCol = sel ? new Vector4(1f, 0.96f, 0.86f, 1f) : new Vector4(0.85f, 0.85f, 0.9f, 1f);
                _hud.TextCentered(b.label, b.x + b.w / 2f, b.y + b.h / 2f - 9 * ui, 2.2f * ui, txtCol);
            }

            // current gold, so BUY GOLD is an informed choice
            string goldLine = MenuGoldLine();
            if (goldLine != null)
            {
                var last = btns[^1];
                _hud.TextCentered(goldLine, W / 2f, last.y + last.h + 36 * ui, 2f * ui, new Vector4(1f, 0.9f, 0.4f, 1f));
            }

            _hud.TextCentered(TouchUI ? "TAP TO CHOOSE" : "ARROWS + ENTER   OR   MOUSE CLICK        M  MUSIC",
                W / 2f, H * 0.9f, 1.5f * ui, dim);
            _hud.End();
        }

        // ---- character-creation screen --------------------------------------
        void RenderCreate()
        {
            int W = FramebufferSize.X, H = FramebufferSize.Y;
            float ui = H / 720f;
            _hud.Begin(W, H);

            float bgA = _level != null ? 0.8f : 1f;
            _hud.Quad(0, 0, W, H, new Vector4(0.03f, 0.03f, 0.05f, bgA));

            var gold = new Vector4(1f, 0.82f, 0.45f, 1f);
            var dim = new Vector4(0.68f, 0.7f, 0.8f, 1f);
            var white = new Vector4(0.96f, 0.96f, 1f, 1f);

            _hud.TextCentered("CREATE YOUR HERO", W / 2f, H * 0.09f, 4f * ui, gold);

            // ---- name field
            var (boxX, boxY, boxW, boxH) = NameBox();
            _hud.TextCentered("NAME", W / 2f, boxY - 30 * ui, 1.6f * ui, dim);
            _hud.Quad(boxX, boxY, boxW, boxH, new Vector4(0.1f, 0.1f, 0.14f, 0.95f));
            Border(boxX, boxY, boxW, boxH, 3 * ui, gold);
            float nScale = 2.6f * ui;
            float nx = boxX + 18 * ui, ny = boxY + (boxH - 8 * nScale) / 2f;
            if (TouchUI && _name.Length == 0 && !_kbOpen)
                _hud.TextCentered("TAP TO ENTER NAME", W / 2f, ny + nScale, nScale * 0.75f, dim);
            _hud.Text(_name, nx, ny, nScale, white);
            bool caret = !TouchUI || _kbOpen; // on touch, the caret means the keyboard is up
            if (caret && ((int)(_time * 2)) % 2 == 0) // blinking caret
                _hud.Quad(nx + _hud.TextWidth(_name, nScale) + 2 * ui, boxY + boxH * 0.22f, 3 * ui, boxH * 0.56f, white);

            // ---- point-buy attributes + live derived preview
            var d = Derive(_str, _dex, _vit, _luk);
            int pts = PointsLeft();
            var (mx, my) = MouseFb();
            float sScale = 1.9f * ui;

            _hud.TextCentered($"POINTS LEFT  {pts}", W / 2f, H * 0.3f, 1.7f * ui,
                pts > 0 ? new Vector4(0.5f, 1f, 0.6f, 1f) : dim);

            var names = new[] { "STRENGTH", "DEXTERITY", "VITALITY", "LUCK" };
            var effects = new[] { $"ATK {d.atkMin}-{d.atkMax}", $"DODGE {d.dodge}%", $"HP {d.maxHp}", $"CRIT {d.crit}%" };
            for (int i = 0; i < 4; i++)
            {
                var r = AttrRow(i);
                bool selRow = i == _createSel;
                if (selRow) _hud.Quad(r.labelX - 12 * ui, r.y - 6 * ui, 620 * ui, 40 * ui, new Vector4(0.2f, 0.16f, 0.08f, 0.7f));
                _hud.Text(names[i], r.labelX, r.y, sScale, selRow ? gold : dim);
                _hud.Text(GetAttr(i).ToString(), r.valueX, r.y, sScale, white);
                _hud.Text(effects[i], r.derivedX, r.y, 1.6f * ui, dim);

                // -/+ buttons
                float by = r.y - 3 * ui;
                bool overMinus = mx >= r.minusX && mx <= r.minusX + r.bw && my >= by && my <= by + r.bh;
                bool overPlus = mx >= r.plusX && mx <= r.plusX + r.bw && my >= by && my <= by + r.bh;
                bool canDec = GetAttr(i) > PbMin, canInc = pts > 0 && GetAttr(i) < PbMax;
                DrawStepper(r.minusX, by, r.bw, r.bh, "-", overMinus, canDec, ui, gold, white);
                DrawStepper(r.plusX, by, r.bw, r.bh, "+", overPlus, canInc, ui, gold, white);
            }

            _hud.TextCentered($"CLASS  {d.cls.ToString().ToUpperInvariant()}", W / 2f, H * 0.71f, 2.2f * ui, gold);

            // ---- buttons
            foreach (var b in CreateButtons())
            {
                bool hover = mx >= b.x && mx <= b.x + b.w && my >= b.y && my <= b.y + b.h;
                var fill = hover ? new Vector4(0.48f, 0.30f, 0.11f, 0.95f) : new Vector4(0.12f, 0.12f, 0.16f, 0.9f);
                _hud.Quad(b.x, b.y, b.w, b.h, fill);
                Border(b.x, b.y, b.w, b.h, 3 * ui, hover ? gold : new Vector4(0.35f, 0.35f, 0.42f, 1f));
                _hud.TextCentered(b.label, b.x + b.w / 2f, b.y + b.h / 2f - 8 * ui, 2f * ui,
                    hover ? new Vector4(1f, 0.96f, 0.86f, 1f) : white);
            }

            if (TouchUI)
            {
                _hud.TextCentered("TAP + / - TO SPEND POINTS", W / 2f, H * 0.9f, 1.35f * ui, dim);
                if (_kbOpen) DrawTouchKeyboard(W, H, ui);
            }
            else
                _hud.TextCentered("TYPE NAME   ARROWS ADJUST   TAB REROLL   ENTER BEGIN   ESC BACK", W / 2f, H * 0.9f, 1.35f * ui, dim);
            _hud.End();
        }

        // Themed push-button (menu style): amber when hot, slate otherwise.
        void DrawButton(float x, float y, float w, float h, string label, bool hot, float ui, float textScale = 2f)
        {
            var gold = new Vector4(1f, 0.82f, 0.45f, 1f);
            var fill = hot ? new Vector4(0.48f, 0.30f, 0.11f, 0.95f) : new Vector4(0.12f, 0.12f, 0.16f, 0.9f);
            _hud.Quad(x, y, w, h, fill);
            Border(x, y, w, h, 3 * ui, hot ? gold : new Vector4(0.35f, 0.35f, 0.42f, 1f));
            _hud.TextCentered(label, x + w / 2f, y + h / 2f - 4 * textScale, textScale,
                hot ? new Vector4(1f, 0.96f, 0.86f, 1f) : new Vector4(0.96f, 0.96f, 1f, 1f));
        }

        static bool Inside(float px, float py, float x, float y, float w, float h)
            => px >= x && px <= x + w && py >= y && py <= y + h;

        void Border(float x, float y, float w, float h, float t, Vector4 c)
        {
            _hud.Quad(x, y, w, t, c);
            _hud.Quad(x, y + h - t, w, t, c);
            _hud.Quad(x, y, t, h, c);
            _hud.Quad(x + w - t, y, t, h, c);
        }

        void DrawStepper(float x, float y, float w, float h, string sym, bool hover, bool enabled, float ui, Vector4 gold, Vector4 white)
        {
            var fill = !enabled ? new Vector4(0.08f, 0.08f, 0.1f, 0.7f)
                     : hover ? new Vector4(0.48f, 0.30f, 0.11f, 0.95f)
                             : new Vector4(0.16f, 0.16f, 0.2f, 0.95f);
            _hud.Quad(x, y, w, h, fill);
            Border(x, y, w, h, 2 * ui, enabled ? (hover ? gold : new Vector4(0.4f, 0.4f, 0.48f, 1f)) : new Vector4(0.25f, 0.25f, 0.3f, 1f));
            var tc = enabled ? white : new Vector4(0.4f, 0.4f, 0.45f, 1f);
            _hud.TextCentered(sym, x + w / 2f, y + h / 2f - 7 * ui, 1.8f * ui, tc);
        }

        // ---- HUD ------------------------------------------------------------
        void RenderHud()
        {
            int W = FramebufferSize.X, H = FramebufferSize.Y;
            float ui = H / 720f; // scale HUD with resolution so it reads the same everywhere
            _hud.Begin(W, H);

            // hurt flash
            if (_screenFlash > 0)
                _hud.Quad(0, 0, W, H, new Vector4(0.8f, 0.05f, 0.05f, _screenFlash * 0.5f));
            if (_descendFlash > 0)
                _hud.Quad(0, 0, W, H, new Vector4(1f, 0.85f, 0.4f, _descendFlash * 0.4f));

            // keep HUD clear of notches / the home indicator
            float sl = SafeInsets.X, st = SafeInsets.Y, sb = SafeInsets.W;

            // ---- hero banner (top-left)
            _hud.Text($"{_name} THE {_className}", 24 * ui + sl, 18 * ui + st, 2f * ui, new Vector4(1f, 0.82f, 0.45f, 1f));
            _hud.Text($"DODGE {_dodgePct}%  CRIT {_critPct}%", 24 * ui + sl, 46 * ui + st, 1.4f * ui, new Vector4(0.7f, 0.72f, 0.8f, 1f));

            // ---- bottom panel: HP bar + stats
            float barW = 320 * ui, barH = 26 * ui, barX = 24 * ui + sl, barY = H - 54 * ui - sb;
            _hud.Quad(barX - 3 * ui, barY - 3 * ui, barW + 6 * ui, barH + 6 * ui, new Vector4(0f, 0f, 0f, 0.6f));
            _hud.Quad(barX, barY, barW, barH, new Vector4(0.25f, 0.05f, 0.06f, 0.9f));
            float frac = _maxHp > 0 ? Math.Clamp(_hp / (float)_maxHp, 0f, 1f) : 0f;
            var hpCol = frac > 0.5f ? new Vector4(0.3f, 0.85f, 0.35f, 1f)
                      : frac > 0.25f ? new Vector4(0.95f, 0.75f, 0.2f, 1f)
                                     : new Vector4(0.95f, 0.25f, 0.2f, 1f);
            _hud.Quad(barX, barY, barW * frac, barH, hpCol);
            _hud.Text($"HP {_hp}/{_maxHp}", barX + 8 * ui, barY + 6 * ui, 1.7f * ui, new Vector4(1, 1, 1, 1));

            _hud.Text($"DEPTH {_depth}", barX, barY - 30 * ui, 2f * ui, new Vector4(0.8f, 0.85f, 1f, 1f));
            _hud.Text($"GOLD {_gold}", barX + 200 * ui, barY - 30 * ui, 2f * ui, new Vector4(1f, 0.9f, 0.4f, 1f));
            int atkLo = _atkMin + _atkBonus, atkHi = _atkMax + _atkBonus;
            var atkCol = _atkBonus > 0 ? new Vector4(0.7f, 1f, 0.8f, 1f) : new Vector4(0.9f, 0.9f, 0.9f, 1f);
            _hud.Text($"ATK {atkLo}-{atkHi}", barX + 400 * ui, barY - 30 * ui, 2f * ui, atkCol);

            // active buffs / charges
            string buffs = "";
            if (_shield > 0) buffs += $"SHIELD {_shield}   ";
            if (_torchTurns > 0) buffs += $"LIGHT {_torchTurns}   ";
            if (_regenTurns > 0) buffs += $"REGEN {_regenTurns}   ";
            if (_freeze > 0) buffs += $"FREEZE {_freeze}   ";
            if (_haste > 0) buffs += $"HASTE {_haste}   ";
            if (buffs.Length > 0)
                _hud.Text(buffs, 24 * ui + sl, 72 * ui + st, 1.4f * ui, new Vector4(0.7f, 0.85f, 1f, 1f));

            // ---- message log (above the bar)
            float ly = barY - 70 * ui;
            for (int i = _log.Count - 1; i >= 0 && i >= _log.Count - 5; i--)
            {
                var m = _log[i];
                float a = Math.Clamp(1.5f - m.Age * 0.3f, 0f, 1f);
                var c = m.Color; c.W = a;
                _hud.Text(m.Text, barX, ly, 1.6f * ui, c);
                ly -= 24 * ui;
            }

            DrawMinimap(W, H, ui);
            DrawCrosshair(W, H, ui);
            if (TouchUI && !_dead) DrawHudButtons(ui);

            // ---- entering-a-new-depth banner (big, centered, fades out)
            if (_levelBanner > 0 && !_dead)
            {
                float a = Math.Clamp(_levelBanner, 0f, 1f); // hold, then fade over the last second
                var col = new Vector4(1f, 0.85f, 0.5f, a);
                var shadow = new Vector4(0f, 0f, 0f, a * 0.6f);
                float cy = H * 0.42f, sc = 5f * ui;
                string msg = $"ENTERING DEPTH {_depth}";
                _hud.TextCentered(msg, W / 2f + 3 * ui, cy + 3 * ui, sc, shadow); // drop shadow
                _hud.TextCentered(msg, W / 2f, cy, sc, col);
            }

            // ---- intro / controls hint
            if (_intro > 0)
            {
                float a = Math.Clamp(_intro, 0f, 1f);
                var hint = new Vector4(1f, 1f, 1f, a * 0.9f);
                if (TouchUI)
                {
                    _hud.TextCentered("SWIPE UP  MOVE / ATTACK     SWIPE DOWN  BACK", W / 2f, H * 0.22f, 1.7f * ui, hint);
                    _hud.TextCentered("SWIPE LEFT / RIGHT  TURN", W / 2f, H * 0.22f + 28 * ui, 1.7f * ui, hint);
                }
                else
                    _hud.TextCentered("WASD MOVE   A/D TURN   Q/E STRAFE   SPACE ATTACK   M MUSIC   F11 WINDOW",
                        W / 2f, H * 0.3f, 1.7f * ui, hint); // below the hero stats and the minimap
            }

            // ---- music-off indicator
            if (!_audio.MusicOn)
                _hud.Text("MUSIC OFF", 24 * ui + sl, H - 84 * ui - sb, 1.4f * ui, new Vector4(0.6f, 0.62f, 0.7f, 0.9f));

            // ---- death screen
            if (_dead)
            {
                _hud.Quad(0, 0, W, H, new Vector4(0.05f, 0f, 0f, 0.6f));
                _hud.TextCentered($"{_name} HAS FALLEN", W / 2f, H / 2f - 80 * ui, 5f * ui, new Vector4(0.95f, 0.2f, 0.2f, 1f));
                _hud.TextCentered($"REACHED DEPTH {_depth}   GOLD {Spendable()}", W / 2f, H / 2f - 10 * ui, 2.2f * ui, new Vector4(1f, 0.9f, 0.6f, 1f));
                int cost = ReviveCost();
                if (TouchUI)
                    DrawDeathButtons(ui);
                else
                {
                    if (Spendable() >= cost)
                        _hud.TextCentered($"PRESS C TO REVIVE  ({cost} GOLD)", W / 2f, H / 2f + 44 * ui, 2.2f * ui, new Vector4(0.5f, 1f, 0.6f, 1f));
                    else
                        _hud.TextCentered($"REVIVE NEEDS {cost} GOLD - NOT ENOUGH", W / 2f, H / 2f + 44 * ui, 1.8f * ui, new Vector4(0.7f, 0.5f, 0.5f, 1f));
                    _hud.TextCentered("PRESS ENTER TO ROLL A NEW HERO", W / 2f, H / 2f + 88 * ui, 1.9f * ui, new Vector4(0.85f, 0.85f, 0.9f, 1f));
                }
            }

            _hud.End();
        }

        void DrawMinimap(int W, int H, float ui)
        {
            float cell = 5f * ui, mw = _level.W * cell, mh = _level.H * cell;
            float ox = W - mw - 18 * ui - SafeInsets.Z, oy = 18 * ui + SafeInsets.Y;
            _hud.Quad(ox - 4 * ui, oy - 4 * ui, mw + 8 * ui, mh + 8 * ui, new Vector4(0f, 0f, 0f, 0.55f));
            for (int x = 0; x < _level.W; x++)
                for (int y = 0; y < _level.H; y++)
                {
                    if (!_seen[x, y]) continue;
                    var t = _level.At(x, y);
                    Vector4 c;
                    if (t == Tile.Wall) c = new Vector4(0.28f, 0.28f, 0.34f, 1f);
                    else if (t == Tile.Exit) c = new Vector4(0.3f, 0.9f, 1f, 1f);
                    else c = new Vector4(0.5f, 0.45f, 0.4f, 1f);
                    _hud.Quad(ox + x * cell, oy + y * cell, cell, cell, c);
                }
            foreach (var e in _enemies)
                if (_seen[e.X, e.Y])
                    _hud.Quad(ox + e.X * cell, oy + e.Y * cell, cell, cell, new Vector4(1f, 0.3f, 0.3f, 1f));
            foreach (var it in _items)
                if (_seen[it.X, it.Y])
                    _hud.Quad(ox + it.X * cell + 1, oy + it.Y * cell + 1, cell - 2, cell - 2, new Vector4(1f, 0.9f, 0.3f, 1f));
            // player
            _hud.Quad(ox + _px * cell - 1 * ui, oy + _py * cell - 1 * ui, cell + 2 * ui, cell + 2 * ui, new Vector4(1f, 1f, 1f, 1f));
        }

        void DrawCrosshair(int W, int H, float ui)
        {
            float cx = W / 2f, cy = H / 2f, s = 8 * ui, t = 2 * ui;
            var c = new Vector4(1f, 1f, 1f, 0.35f);
            _hud.Quad(cx - s, cy - t / 2, s * 2, t, c);
            _hud.Quad(cx - t / 2, cy - s, t, s * 2, c);
        }

        // ---- helpers --------------------------------------------------------
        void MarkSeen()
        {
            const int R = 5;
            for (int dx = -R; dx <= R; dx++)
                for (int dy = -R; dy <= R; dy++)
                {
                    int x = _px + dx, y = _py + dy;
                    if ((uint)x >= (uint)_level.W || (uint)y >= (uint)_level.H) continue;
                    if (dx * dx + dy * dy <= R * R) _seen[x, y] = true;
                }
        }

        Enemy EnemyAt(int x, int y) { foreach (var e in _enemies) if (e.X == x && e.Y == y) return e; return null; }
        Item ItemAt(int x, int y) { foreach (var it in _items) if (it.X == x && it.Y == y) return it; return null; }

        void Log(string s, Vector4 c)
        {
            _log.Add(new Msg { Text = s, Color = c, Age = 0 });
            if (_log.Count > 12) _log.RemoveAt(0);
        }

        void Shuffle<T>(IList<T> l)
        {
            for (int i = l.Count - 1; i > 0; i--) { int j = _rng.Next(i + 1); (l[i], l[j]) = (l[j], l[i]); }
        }

        static int Manhattan(int ax, int ay, int bx, int by) => Math.Abs(ax - bx) + Math.Abs(ay - by);
        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static float Smooth(float t) => t * t * (3f - 2f * t);
        static float Approach(float v, float target, float step)
        {
            if (v < target) return Math.Min(target, v + step);
            if (v > target) return Math.Max(target, v - step);
            return v;
        }

        /// <summary>The app is going to the background: drop to the menu, which saves the run.</summary>
        public void Suspend()
        {
            if (!_inMenu && !_inCreate && !_dead && _level != null) EnterMenu();
            else SaveGame();
        }

        public void Unload()
        {
            SaveGame(); // persist the run if the window is closed mid-game
            _world?.Dispose(); _sprite?.Dispose();
            _floor?.Dispose(); _bill?.Dispose();
            _wallsMesh?.Dispose(); _floorMesh?.Dispose(); _ceilMesh?.Dispose();
            _wallTex?.Dispose(); _floorTex?.Dispose(); _ceilTex?.Dispose(); _exitTex?.Dispose();
            _wallNrm?.Dispose(); _floorNrm?.Dispose(); _ceilNrm?.Dispose(); _flatNrm?.Dispose();
            _audio?.Dispose();
        }

        // =====================================================================
        // Shaders (GLSL 330 core, valid under a 4.1 core context).
        const string WorldVS = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNormal;
layout(location=2) in vec2 aUV;
uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProj;
out vec3 vWorld; out vec3 vNormal; out vec2 vUV;
void main(){
    vec4 world = vec4(aPos,1.0) * uModel;
    vWorld = world.xyz;
    vNormal = aNormal * mat3(uModel);
    vUV = aUV;
    gl_Position = world * uView * uProj;
}";

        const string WorldFS = @"#version 330 core
in vec3 vWorld; in vec3 vNormal; in vec2 vUV;
uniform sampler2D uTex;
uniform sampler2D uNormal;
uniform vec3 uLightPos;
uniform float uTorch;
uniform float uBump;
out vec4 frag;

// Per-pixel tangent frame from screen-space derivatives (no tangent attribute needed).
mat3 cotangentFrame(vec3 N, vec3 p, vec2 uv){
    vec3 dp1 = dFdx(p), dp2 = dFdy(p);
    vec2 duv1 = dFdx(uv), duv2 = dFdy(uv);
    vec3 dp2perp = cross(dp2, N);
    vec3 dp1perp = cross(N, dp1);
    vec3 T = dp2perp * duv1.x + dp1perp * duv2.x;
    vec3 B = dp2perp * duv1.y + dp1perp * duv2.y;
    float invmax = inversesqrt(max(dot(T,T), dot(B,B)));
    return mat3(T*invmax, B*invmax, N);
}

void main(){
    vec3 tex = texture(uTex, vUV).rgb;
    vec3 N = normalize(vNormal);
    // perturb the geometric normal with the tangent-space bump
    vec3 nt = texture(uNormal, vUV).rgb * 2.0 - 1.0;
    nt.xy *= uBump;
    N = normalize(cotangentFrame(N, vWorld, vUV) * nt);

    vec3 L = uLightPos - vWorld;
    float dist = length(L);
    L /= max(dist, 0.001);
    float diff = max(dot(N, L), 0.0);
    float atten = uTorch / (1.0 + 0.22*dist + 0.16*dist*dist);
    // torch sits at the eye, so the view and light dirs coincide -> N.L drives the glint
    float spec = pow(diff, 24.0) * 0.35;
    vec3 torch = vec3(1.0, 0.82, 0.55);
    float ambient = 0.06;
    vec3 col = tex * (ambient + (0.35 + 0.9*diff) * atten) + spec * atten * torch;
    col *= mix(vec3(1.0), torch, 0.75);
    frag = vec4(col, 1.0);
}";

        const string SpriteFS = @"#version 330 core
in vec3 vWorld; in vec3 vNormal; in vec2 vUV;
uniform sampler2D uTex;
uniform vec3 uLightPos;
uniform vec3 uTint;
uniform float uTorch;
out vec4 frag;
void main(){
    vec4 t = texture(uTex, vUV);
    if (t.a < 0.4) discard;
    float dist = length(uLightPos - vWorld);
    float atten = uTorch / (1.0 + 0.20*dist + 0.14*dist*dist);
    vec3 torch = vec3(1.0, 0.82, 0.55);
    vec3 col = t.rgb * (0.14 + 1.2*atten);
    col *= mix(vec3(1.0), torch, 0.5);
    col *= uTint;
    frag = vec4(col, 1.0);
}";
    }
}
