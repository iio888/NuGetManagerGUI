using System;
using System.Collections.Generic;
using System.Text;

namespace NugetManagerGUI.Model
{
    internal class Source
    {
        public Source(string url, string? api)
        {
            Url = url;
            Api = api;
        }

        public string Url { get; }
        public string? Api { get; }
    }
}
