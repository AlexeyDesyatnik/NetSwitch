using System.Collections.Generic;
using NetSwitch.Model;

namespace NetSwitch.Windows
{
    internal sealed class WindowsNetworkSystem : INetworkSystem
    {
        public IList<Adapter> ReadAdapters()
        {
            return AdapterReader.ReadAll();
        }

        public ProxyState ReadProxy()
        {
            InteractiveUser user = InteractiveUserReader.Read();
            return new ProxyState(
                user,
                ProxyReader.ReadSystemProxy(user.Sid),
                ProxyReader.ReadWinHttp(),
                ProxyReader.ReadPolicy(user.Sid));
        }
    }
}
