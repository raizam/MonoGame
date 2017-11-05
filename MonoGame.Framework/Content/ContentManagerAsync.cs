using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;

namespace Microsoft.Xna.Framework.Content
{
#if USE_GAMETASK
    public partial class ContentManager
    {
        static private BackgroundThread _fileSystemBackgroundThread;
        private IGameTaskConsumer _backgroundThreadConsumer;

        protected virtual GameTask ReloadAssetAsync<T>(string originalAssetName, T currentAsset)
        {
            string assetName = originalAssetName;
            if (string.IsNullOrEmpty(assetName))
            {
                throw new ArgumentNullException("assetName");
            }
            if (disposed)
            {
                throw new ObjectDisposedException("ContentManager");
            }

            if (this.graphicsDeviceService == null)
            {
                this.graphicsDeviceService = serviceProvider.GetService(typeof(IGraphicsDeviceService)) as IGraphicsDeviceService;
                if (this.graphicsDeviceService == null)
                {
                    throw new InvalidOperationException("No Graphics Device Service");
                }
            }

            return OpenStreamAsync(assetName).Then(t =>
            {
                //Executed in Game thread.
                var stream = t.Result;
                using (var xnbReader = new BinaryReader(stream))
                {
                    using (var reader = GetContentReaderFromXnb(assetName, stream, xnbReader, null))
                    {
                        reader.ReadAsset<T>(currentAsset);
                    }
                }
            });

        }

        protected GameTask<T> ReadAssetAsync<T>(string assetName, Action<IDisposable> recordDisposableObject)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                throw new ArgumentNullException("assetName");
            }
            if (disposed)
            {
                throw new ObjectDisposedException("ContentManager");
            }

            string originalAssetName = assetName;
            object result = null;

            if (this.graphicsDeviceService == null)
            {
                this.graphicsDeviceService = serviceProvider.GetService(typeof(IGraphicsDeviceService)) as IGraphicsDeviceService;
                if (this.graphicsDeviceService == null)
                {
                    throw new InvalidOperationException("No Graphics Device Service");
                }
            }

            // Loading the XNB file asyncronously
            return OpenStreamAsync(assetName).Then(g =>
            {
                //here we are back in the game thread, we can now rely on GraphicsDevice safely and execute the ContentReader
                var stream = g.Result;
                using (var xnbReader = new BinaryReader(stream))
                {
                    using (var reader = GetContentReaderFromXnb(assetName, stream, xnbReader, recordDisposableObject))
                    {
                        result = reader.ReadAsset<T>();
                        if (result is GraphicsResource)
                            ((GraphicsResource)result).Name = originalAssetName;
                    }
                }

                if (result == null)
                    throw new ContentLoadException("Could not load " + originalAssetName + " asset!");

                return (T)result;
            });

        }

        public virtual GameTask<T> LoadAsync<T>(string assetName)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                throw new ArgumentNullException("assetName");
            }
            if (disposed)
            {
                throw new ObjectDisposedException("ContentManager");
            }

            // On some platforms, name and slash direction matter.
            // We store the asset by a /-seperating key rather than how the
            // path to the file was passed to us to avoid
            // loading "content/asset1.xnb" and "content\\ASSET1.xnb" as if they were two 
            // different files. This matches stock XNA behavior.
            // The dictionary will ignore case differences
            var key = assetName.Replace('\\', '/');

            // Check for a previously loaded asset first
            object asset = null;
            if (loadedAssets.TryGetValue(key, out asset))
            {
                if (asset is T)
                {
                    return GameTask.FromResult<T>((T)asset);
                }
            }

            // Load the asset asynchonously
            return ReadAssetAsync<T>(assetName, null).Then(t =>
            {
                //executed in main thread
                T result = t.Result;
                loadedAssets[key] = result;
                return result;
            });
        }

        protected virtual GameTask<Stream> OpenStreamAsync(string assetName)
        {
            if (this._backgroundThreadConsumer == null)
                this._backgroundThreadConsumer = (IGameTaskConsumer)serviceProvider.GetService(typeof(IGameTaskConsumer));

            //send the FileSystem read operation to the fileSystem thread
            return _fileSystemBackgroundThread.Execute(() =>
            {
                //~~Executed in Background thread~~
                Stream assetStream = OpenStream(assetName);

                //if the returned stream is a MemoryStream, we return it.
                //Otherwise, we want to load the asset in memory and release the file handle now.
                if (assetStream is MemoryStream)
                    return assetStream;

                MemoryStream memStream = new MemoryStream();
                assetStream.CopyTo(memStream);
                memStream.Seek(0, SeekOrigin.Begin);
                assetStream.Close();
                assetStream.Dispose();
                return memStream;

            });
        }
    }
#endif //USE_GAMETASK
}
