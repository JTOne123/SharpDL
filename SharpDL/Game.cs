﻿using Microsoft.Extensions.Logging;
using SDL2;
using SharpDL.Events;
using SharpDL.Graphics;
using SharpDL.Input;
using System;

namespace SharpDL
{
    public abstract class Game : IDisposable
    {
        private readonly ILogger<Game> logger;

        #region Members

        private const uint EMPTY_UINT = 0;
        private const int EMPTY_INT = -1;
        private const float FRAMES_PER_SECOND = 60f;
        private readonly GameTime gameTime = new GameTime();
        private readonly Timer gameTimer = new Timer();

        private TimeSpan accumulatedElapsedTime = TimeSpan.Zero;
        private readonly TimeSpan targetElapsedTime = TimeSpan.FromSeconds(1 / FRAMES_PER_SECOND);
        private readonly TimeSpan maxElapsedTime = TimeSpan.FromSeconds(0.5);
        private bool isFrameRateCapped = true;

        #endregion Members

        #region Properties

        protected EventManager EventManager { get; private set; }

        protected Window Window { get; private set; }

        protected Renderer Renderer { get; private set; }

        protected bool IsActive { get; private set; }

        protected bool IsExiting { get; private set; }

        #endregion Properties

        #region Constructors

        /// <summary>Default constructor of the base Game class does nothing. Only when Initialize is called
        /// is anything useful done.
        /// </summary>
        public Game(ILogger<Game> logger = null)
        {
            this.logger = logger;
            EventManager = new EventManager();
            EventManager.WindowClosed += OnExiting;
            EventManager.Quitting += OnExiting;
        }

        #endregion Constructors

        #region Events

        private void OnExiting(object sender, GameEventArgs e)
        {
            IsExiting = true;
        }

        #endregion Events

        #region Game Cycle Control

        /// <summary>Begins the game by performing the following cycle events in this order: Initialize, LoadContent,
        /// CheckInputs, Update, Draw, UnloadContent.
        /// </summary>
        public void Run(InitializeType types = InitializeType.Everything)
        {
            PerformInitialize(types);
            LoadContent();

            while (!IsExiting)
            {
                SDL.SDL_Event rawEvent = new SDL.SDL_Event();
                while (SDL.SDL_PollEvent(out rawEvent) == 1)
                {
                    EventManager.RaiseEvent(rawEvent);
                }
             
                Tick();
            }

            UnloadContent();
            Dispose();
        }

        /// <summary>A tick is equal to a single time step forward in the game state. During each tick, the game will update total game time,
        /// elapsed update time, and frame rates. It is important to note that the implementation is based on a Fixed Time Step algorithm where
        /// each update and draw occur in the same constant fixed intervals. Additionally, the game will call the Update and Draw game cycle
        /// methods to be overridden by each implementation's specific Game Update and Draw logic. This method is based heavily on MonoGame's
        /// tick implementation and suggestions from Glenn Fiedler's blog (http://gafferongames.com/game-physics/fix-your-timestep/).
        /// </summary>
        private void Tick()
        {
            // If our frame rate is capped, we want to wait until we have elapsed enough time to have a fixed-step
            // At 60 FPS, the target elapsed time is 1/60 or 0.01667~ seconds.
            while (isFrameRateCapped && (accumulatedElapsedTime < targetElapsedTime))
            {
                accumulatedElapsedTime += gameTimer.ElapsedTime;
                gameTimer.Start();

                if (isFrameRateCapped && (accumulatedElapsedTime < targetElapsedTime))
                {
                    // Sleep for as long as we need to reach the target elapsed time
                    TimeSpan sleepTime = targetElapsedTime - accumulatedElapsedTime;
                    SDL.SDL_Delay((uint)sleepTime.TotalMilliseconds);
                }
            }

            // Don't allow any updates to go beyond the max update time
            if (accumulatedElapsedTime > maxElapsedTime)
                accumulatedElapsedTime = maxElapsedTime;

            // Fixed time step update
            if (isFrameRateCapped)
            {
                int stepCount = 0;

                // If we have waited longer than the target time (non-precision timers/waits?), we need to advance
                // the game state in a fixed step interval.
                while (accumulatedElapsedTime >= targetElapsedTime)
                {
                    gameTime.TotalGameTime += targetElapsedTime;
                    accumulatedElapsedTime -= targetElapsedTime;
                    stepCount++;

                    PerformUpdate(gameTime);
                }

                // In normal scenarios, this will advance the elapsed time by the target, but in cases where
                // we have had to "catch up" because of non-precise waits, we will need to take the fixed steps
                // into account.
                gameTime.ElapsedGameTime = TimeSpan.FromTicks(targetElapsedTime.Ticks * stepCount);
            }
            // Variable time step update
            else
            {
                gameTime.ElapsedGameTime = accumulatedElapsedTime;
                gameTime.TotalGameTime += targetElapsedTime;
                accumulatedElapsedTime = TimeSpan.Zero;
                
                PerformUpdate(gameTime);
            }

            Draw(gameTime);
        }

        /// <summary>Raises the Exiting event and disposes of this instance.
        /// </summary>
        public void Quit()
        {
            IsExiting = true;
            EventManager.RaiseExiting(this, EventArgs.Empty);
        }

        #endregion Game Cycle Control

        #region Game Cycle

        /// <summary>Override to initialize any custom objects or large helpers that are required by the game.
        /// </summary>
        protected abstract void Initialize();

        /// <summary>
        /// Template Method Pattern to require initialize of the game engine before calling the game's custom
        /// initialize method.
        /// </summary>
        /// <param name="types"></param>
        private void PerformInitialize(InitializeType types)
        {
            // Initialize base SDL before the game's custom initialize
            InitializeBase(types);
            Initialize();
        }

        /// <summary>Initializes the game by calling initialize on the SDL2 instance with the passed flags
        /// or "EVERYTHING" if 0. Additionally, this method will initialize SDL_ttf and SDL_image to load fonts and images.
        /// </summary>
        /// <param name="types">Bit flags indicating the way in which SDL should be initialized</param>
        private void InitializeBase(InitializeType types)
        {
            if (SDL.SDL_Init((uint)types) != 0)
            {
                throw new InvalidOperationException($"SDL_Init: {SDL.SDL_GetError()}");
            }

            if (SDL_ttf.TTF_Init() != 0)
            {
                throw new InvalidOperationException($"TTF_Init: {SDL.SDL_GetError()}");
            }

            SDL_image.IMG_InitFlags initImageFlags = 
                SDL_image.IMG_InitFlags.IMG_INIT_JPG 
                | SDL_image.IMG_InitFlags.IMG_INIT_PNG
                | SDL_image.IMG_InitFlags.IMG_INIT_TIF
                | SDL_image.IMG_InitFlags.IMG_INIT_WEBP; 
            int initImageResult = SDL_image.IMG_Init(initImageFlags);
            if ((initImageResult & (int)initImageFlags) != (int)initImageFlags)
            {
                throw new InvalidOperationException($"IMG_Init: {SDL.SDL_GetError()}");
            }
        }

        /// <summary>Used for potentially long lasting operations that should only occur relatively rarely. Usually, this
        /// method is used to load images, textures, maps, sounds, videos, and other game assets at the beginning of a level or area.
        /// </summary>
        protected abstract void LoadContent();

        /// <summary>Update the state of the game such as positions, health, entity properties, and more.
        /// This is called before Draw in the main game loop.
        /// </summary>
        /// <param name="gameTime">Allows access to total game time and elapsed game time since the last update</param>
        protected abstract void Update(GameTime gameTime);

        private void PerformUpdate(GameTime gameTime)
        {
            Mouse.UpdateMouseState();
            Update(gameTime);
        }

        /// <summary>Draw the current state of the game such as textures, surfaces, maps, and other visual content.
        /// This is called after Update in the main game loop.
        /// </summary>
        /// <param name="gameTime">Allows access to total game time and elapsed game time since the last update</param>
        protected abstract void Draw(GameTime gameTime);

        /// <summary>Used to unload game assets that were loaded during the LoadContent method. Usually, you use this to free
        /// any resources that should not be lingering any longer or are no longer required.
        /// </summary>
        protected abstract void UnloadContent();

        #endregion Game Cycle

        #region Initializers

        /// <summary>Creates a SDL window to render content within.
        /// </summary>
        /// <param name="title">Title of the window</param>
        /// <param name="x">X position of the top left corner</param>
        /// <param name="y">Y position of the top left corner</param>
        /// <param name="width">Width of the window</param>
        /// <param name="height">Height of the window</param>
        /// <param name="flags">Bit flags indicating the way in which the window should be created</param>
        protected void CreateWindow(string title, int x, int y, int width, int height, WindowFlags flags)
        {
            Window = new Window(title, x, y, width, height, flags);
        }

        /// <summary>Creates a SDL Renderer to copy and draw textures to a window
        /// </summary>
        /// <param name="flags">Bit flags indicating the way in which the renderer should be created</param>
        protected void CreateRenderer(RendererFlags flags)
        {
            CreateRenderer(EMPTY_INT, flags);
        }

        /// <summary>Creates a SDL Renderer to copy and draw textures to a window
        /// </summary>
        /// <param name="index">Index of the renderering driver. -1 to choose the first available.</param>
        /// <param name="flags">Bit flags indicating the way in which the renderer should be created</param>
        protected void CreateRenderer(int index, RendererFlags flags)
        {
            if (Window == null)
            {
                throw new InvalidOperationException("Window has not been initialized. You must first create a Window before creating a Renderer.");
            }

            Renderer = new Renderer(this.Window, index, flags);

            SDL2.SDL.SDL_SetHint(SDL2.SDL.SDL_HINT_RENDER_SCALE_QUALITY, "linear");
        }

        #endregion Initializers

        #region Dispose

        public virtual void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~Game()
        {
            Dispose(false);
        }

        /// <summary>Override to dispose of any custom objects that you've instantiated. Always call
        /// base.Dispose() so that the base class objects are disposed as well.
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (Window != null)
            {
                Window.Dispose();
            }

            if (Renderer != null)
            {
                Renderer.Dispose();
            }

            SDL_ttf.TTF_Quit();
            SDL_image.IMG_Quit();
            SDL.SDL_Quit();
        }

        #endregion Dispose
    }
}