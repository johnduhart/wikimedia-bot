using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Text;

namespace wmib
{
    public partial class Infobot
    {
        /// <summary>
        /// Data file
        /// </summary>
        private string datafile_raw = "";
        private string datafile_xml = "";
        private string temporary_data = "";
        public bool Sensitive = true;
        public bool stored = true;
        public static string DefaultPrefix = "!";
        public string prefix = "!";

        private Thread tSearch = null;
        public Thread SnapshotManager = null;
        private Module Parent;

        // if we need to update dump
        public bool update = true;

        public static Channel ReplyChan = null;

        public static DateTime NA = DateTime.MaxValue;

        /// <summary>
        /// List of all items in class
        /// </summary>
        public List<InfobotKey> Keys = new List<InfobotKey>();

        /// <summary>
        /// List of all aliases we want to use
        /// </summary>
        public List<InfobotAlias> Alias = new List<InfobotAlias>();

        private Channel pChannel;

        private string search_key;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="database"></param>
        /// <param name="channel"></param>
        public Infobot(string database, Channel channel, Module module, bool sensitive = true)
        {
            Sensitive = sensitive;
            datafile_xml = database + ".xml";
            datafile_raw = database;
            pChannel = channel;
            Parent = module;
            prefix = Module.GetConfig(pChannel, "Infobot.Prefix", DefaultPrefix);
            LoadData();
        }

        private bool AliasExists(string name, bool sensitive = true)
        {
            if (sensitive)
            {
                lock (this)
                {
                    foreach (InfobotAlias key in Alias)
                    {
                        if (key.Name == name)
                        {
                            return true;
                        }
                    }
                }
            }
            if (!sensitive)
            {
                name = name.ToLower();
                lock (this)
                {
                    foreach (InfobotAlias key in Alias)
                    {
                        if (key.Name.ToLower() == name)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Function returns true if key exists
        /// </summary>
        /// <param name="name">Name of key</param>
        /// <param name="sensitive">If bot is sensitive or not</param>
        /// <returns></returns>
        public bool KeyExists(string name, bool sensitive = true)
        {
            if (!sensitive)
            {
                name = name.ToLower();
                lock (this)
                {
                    foreach (InfobotKey key in Keys)
                    {
                        if (key.Key.ToLower() == name)
                        {
                            return true;
                        }
                    }
                }
            }
            if (sensitive)
            {
                lock (this)
                {
                    foreach (InfobotKey key in Keys)
                    {
                        if (key.Key == name)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        public InfobotKey GetKey(string name, bool sensitive = true)
        {
            if (!sensitive)
            {
                lock (this)
                {
                    name = name.ToLower();
                    foreach (InfobotKey key in Keys)
                    {
                        if (key.Key.ToLower() == name)
                        {
                            return key;
                        }
                    }
                }
            }
            if (sensitive)
            {
                lock (this)
                {
                    foreach (InfobotKey key in Keys)
                    {
                        if (key.Key == name)
                        {
                            return key;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// @infobot-detail
        /// </summary>
        /// <param name="key"></param>
        /// <param name="chan"></param>
        public void InfobotDetail(string key, Channel chan)
        {
            InfobotKey CV = GetKey(key, Sensitive);
            if (CV == null)
            {
                chan.PrimaryInstance.irc.Queue.DeliverMessage("There is no such a key", chan, IRC.priority.low);
                return;
            }
            if (CV.Key == key)
            {
                string created = "N/A";
                string last = "N/A";
                string name = "N/A";
                if (CV.LastTime != NA)
                {
                    TimeSpan span = DateTime.Now - CV.LastTime;
                    last = CV.LastTime.ToString() + " (" + span.ToString() + " ago)";
                }
                if (CV.CreationTime != NA)
                {
                    created = CV.CreationTime.ToString();
                }
                if (CV.User != "")
                {
                    name = CV.User;
                }
                string type = " this key is normal";
                if (CV.Raw)
                {
                    type = " this key is raw";
                }
                Core.irc.Queue.DeliverMessage(messages.Localize("infobot-data", chan.Language, new List<string> {key, name, created, CV.Displayed.ToString(),
                        last + type }), chan, IRC.priority.low);
                return;
            }
        }

        public List<InfobotKey> SortedItem()
        {
            List<InfobotKey> OriginalList = new List<InfobotKey>();
            List<InfobotKey> Item = new List<InfobotKey>();
            int keycount;
            lock (this)
            {
                keycount = Keys.Count;
                OriginalList.AddRange(Keys);
            }
            try
            {
                if (keycount > 0)
                {
                    List<string> Name = new List<string>();
                    foreach (InfobotKey curr in OriginalList)
                    {
                        Name.Add(curr.Key);
                    }
                    Name.Sort();
                    foreach (string f in Name)
                    {
                        foreach (InfobotKey g in OriginalList)
                        {
                            if (f == g.Key)
                            {
                                Item.Add(g);
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception fail)
            {
                Parent.HandleException(fail);
                Parent.Log("Exception while creating list for html");
            }
            return Item;
        }

        private static string ParseInfo(string key, string[] pars, string original, InfobotKey Key)
        {
            string keyv = key;
            bool raw = false;
            if (Key != null)
            {
                raw = Key.Raw;
            }
            if (pars.Length > 1)
            {
                string keys = "";
                int curr = 1;
                while (pars.Length > curr)
                {
                    if (!raw)
                    {
                        keyv = keyv.Replace("$" + curr.ToString(), pars[curr]);
                        keyv = keyv.Replace("$url_encoded_" + curr.ToString(), System.Web.HttpUtility.UrlEncode(pars[curr]));
                        keyv = keyv.Replace("$wiki_encoded_" + curr.ToString(), System.Web.HttpUtility.UrlEncode(pars[curr]).Replace("+", "_").Replace("%3a", ":").Replace("%2f", "/").Replace("%28", "(").Replace("%29", ")"));
                    }
                    if (keys == "")
                    {
                        keys = pars[curr];
                    }
                    else
                    {
                        keys = keys + " " + pars[curr];
                    }
                    curr++;
                }
                if (original.Contains ("|") && !raw)
                {
                    original = original.Substring (0, original.IndexOf ("|"));
                    original = original.Trim ();
                }
                keyv = keyv.Replace("$*", original);
                keyv = keyv.Replace("$url_encoded_*", System.Web.HttpUtility.UrlEncode(original));
                keyv = keyv.Replace("$wiki_encoded_*", System.Web.HttpUtility.UrlEncode(original).Replace("+", "_").Replace("%3a", ":").Replace("%2f", "/").Replace("%28", "(").Replace("%29", ")"));
            }
            return keyv;
        }

        public static bool Linkable(Channel host, Channel guest)
        {
            if (host == null)
            {
                return false;
            }
            if (guest == null)
            {
                return false;
            }
            if (host.SharedLinkedChan.Contains(guest))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Get value of key
        /// </summary>
        /// <param name="key">Key</param>
        /// <returns></returns>
        private string GetValue(string key)
        {
            lock (this)
            {
                if (Sensitive)
                {
                    foreach (InfobotKey data in Keys)
                    {
                        if (data.Key == key)
                        {
                            data.LastTime = DateTime.Now;
                            data.Displayed++;
                            stored = false;
                            return data.Text;
                        }
                    }
                    return "";
                }
                string key2 = key.ToLower();
                foreach (InfobotKey data in Keys)
                {
                    if (data.Key.ToLower() == key2)
                    {
                        data.LastTime = DateTime.Now;
                        data.Displayed++;
                        stored = false;
                        return data.Text;
                    }
                }
            }
            return "";
        }

        /// <summary>
        /// Determines whether this key is ignored for channel
        /// </summary>
        /// <returns>
        /// <c>true</c> if this instance is ignored the specified name; otherwise, <c>false</c>.
        /// </returns>
        /// <param name='name'>
        /// If set to <c>true</c> name.
        /// </param>
        private bool IsIgnored(string name, Channel channel)
        {
            string ignore_test = name;
            if (ignore_test.Contains(" "))
            {
                ignore_test = ignore_test.Substring(0, ignore_test.IndexOf(" "));
            }
            return (channel.Infobot_IgnoredNames.Contains(ignore_test));
        }

        /// <summary>
        /// Print a value to channel if found, this message doesn't need to be a valid command for it to work
        /// </summary>
        /// <param name="name">Name</param>
        /// <param name="user">User</param>
        /// <param name="chan">Channel</param>
        /// <param name="host">Host name</param>
        /// <returns></returns>
        public bool InfobotExec(string name, string user, Channel chan, string host)
        {
            try
            {
                // check if it starts with the prefix
                if (!name.StartsWith(prefix))
                {
                    return true;
                }
                // check if this channel is allowed to access the db
                Channel data = RetrieveMasterDBChannel(chan);
                bool Allowed = (data != null);
                // handle prefix
                name = name.Substring(1);
                Infobot infobot = null;
                if (Allowed)
                {
                    infobot = (Infobot)data.RetrieveObject("Infobot");
                }

                // check if key is ignored
                if (IsIgnored(name, chan))
                {
                    return true;
                }

                // split by parameters so we can easily get the arguments user provided
                List<string> Parameters = new List<string>(name.Split(' '));
                
                string key = Parameters[0];

                // check if key has some parameters or command
                if (Parameters.Count > 1)
                {
                    // someone want to create a new key
                    if (Parameters[1] == "is")
                    {
                        // check if they are approved to do that
                        if (chan.SystemUsers.IsApproved(user, host, "info"))
                        {
                            if (!Allowed)
                            {
                                // check if we can deliver error message
                                if (!chan.SuppressWarnings)
                                {
                                    Core.irc.Queue.DeliverMessage(messages.Localize("db7", chan.Language), chan);
                                }
                                return true;
                            }
                            // they can but there is only 1 parameter and we need at least 2
                            if (Parameters.Count < 3)
                            {
                                if (!chan.SuppressWarnings)
                                {
                                    Core.irc.Queue.DeliverMessage(messages.Localize("key", chan.Language), chan);
                                }
                                return true;
                            }
                            // check if there is pipe symbol in the key, which is not a valid symbol
                            if (Parameters[0].Contains("|"))
                            {
                                if (!chan.SuppressWarnings)
                                {
                                    Core.irc.Queue.DeliverMessage("Invalid symbol in the key", chan);
                                }
                                return true;
                            }
                            // store the key
                            infobot.SetKey(key, Parameters[0], user, chan);
                            return true;
                        }
                        else
                        {
                            if (!chan.SuppressWarnings)
                            {
                                // user can't make the key
                                Core.irc.Queue.DeliverMessage(messages.Localize("Authorization", chan.Language), chan);
                            }
                        }
                        return false;
                    }
                    // alias
                    bool force = false;
                    if (Parameters[1] == "alias" || Parameters[1] == "force-alias")
                    {
                        if (Parameters[1] == "force-alias")
                        {
                            force = true;
                        }
                        if (chan.SystemUsers.IsApproved(user, host, "info"))
                        {
                            if (!Allowed)
                            {
                                if (!chan.SuppressWarnings)
                                {
                                    Core.irc.Queue.DeliverMessage(messages.Localize("db7", chan.Language), chan);
                                }
                                return true;
                            }
                            if (Parameters.Count < 3)
                            {
                                if (!chan.SuppressWarnings)
                                {
                                    Core.irc.Queue.DeliverMessage(messages.Localize("InvalidAlias", chan.Language), chan);
                                }
                                return true;
                            }
                            if (infobot != null)
                            {
                                infobot.aliasKey(name.Substring(name.IndexOf(" alias") + 7), Parameters[0], "", chan, force);
                                return true;
                            }
                        }
                        else
                        {
                            if (!chan.SuppressWarnings)
                            {
                                Core.irc.Queue.DeliverMessage(messages.Localize("Authorization", chan.Language), chan);
                            }
                        }
                        return false;
                    }
                    if (Parameters[1] == "unalias")
                    {
                        if (chan.SystemUsers.IsApproved(user, host, "info"))
                        {
                            if (!Allowed)
                            {
                                if (!chan.SuppressWarnings)
                                {
                                    Core.irc.Queue.DeliverMessage(messages.Localize("db7", chan.Language), chan);
                                }
                                return true;
                            }
                            if (infobot != null)
                            {
                                lock (infobot)
                                {
                                    foreach (InfobotAlias b in infobot.Alias)
                                    {
                                        if (b.Name == Parameters[0])
                                        {
                                            infobot.Alias.Remove(b);
                                            Core.irc.Queue.DeliverMessage(messages.Localize("AliasRemoved", chan.Language), chan);
                                            infobot.stored = false;
                                            return false;
                                        }
                                    }
                                }
                            }
                            return false;
                        }
                        if (!chan.SuppressWarnings)
                        {
                            Core.irc.Queue.DeliverMessage(messages.Localize("Authorization", chan.Language), chan);
                        }
                        return false;
                    }
                    // remove key
                    if (Parameters[1] == "del")
                    {
                        if (chan.SystemUsers.IsApproved(user, host, "info"))
                        {
                            if (!Allowed)
                            {
                                Core.irc.Queue.DeliverMessage(messages.Localize("db7", chan.Language), chan);
                                return true;
                            }
                            if (infobot != null)
                            {
                                infobot.rmKey(Parameters[0], "", chan);
                            }
                        }
                        else
                        {
                            if (!chan.SuppressWarnings)
                            {
                                Core.irc.Queue.DeliverMessage(messages.Localize("Authorization", chan.Language), chan);
                            }
                        }
                        return false;
                    }
                }
                if (!Allowed)
                {
                    // there is no DB we could read data from
                    return true;
                }
                string User = "";
                key = Parameters[0];
                if (name.Contains("|"))
                {
                    User = name.Substring(name.IndexOf("|") + 1);
                    if (Module.GetConfig(chan, "Infobot.Trim-white-space-in-name", true))
                    {
                        User = User.Trim();
                    }
                    name = name.Substring(0, name.IndexOf("|"));
                }
                string[] p = name.Split(' ');
                int parameters = p.Length;
                string keyv = "";
                if (infobot != null)
                {
                    keyv = infobot.GetValue(p[0]);
                }
                InfobotKey _key = GetKey(p[0]);
                bool raw = false;
                if (_key != null)
                {
                    if (_key.Raw)
                    {
                        raw = _key.Raw;
                        name = key;
                        User = "";
                    }
                }
                if (keyv != "")
                {
                    keyv = ParseInfo(keyv, p, key, _key);
                    if (User == "")
                    {
                        Core.irc.Queue.DeliverMessage(keyv, chan);
                    }
                    else
                    {
                        Core.irc.Queue.DeliverMessage(User + ": " + keyv, chan);
                    }
                    return true;
                }
                if (infobot != null)
                {
                    lock (infobot)
                    {
                        foreach (InfobotAlias b in infobot.Alias)
                        {
                            if (Sensitive)
                            {
                                if (b.Name == p[0])
                                {
                                    keyv = infobot.GetValue(b.Key);
                                    if (keyv != "")
                                    {
                                        keyv = ParseInfo(keyv, p, key, _key);
                                        if (User == "")
                                        {
                                            Core.irc.Queue.DeliverMessage(keyv, chan);
                                        }
                                        else
                                        {
                                            Core.irc.Queue.DeliverMessage(User + ": " + keyv, chan);
                                        }
                                        return true;
                                    }
                                }
                            }
                            else
                            {
                                if (b.Name.ToLower() == p[0].ToLower())
                                {
                                    keyv = infobot.GetValue(b.Key);
                                    if (keyv != "")
                                    {
                                        keyv = ParseInfo(keyv, p, key, _key);
                                        if (User == "")
                                        {
                                            Core.irc.Queue.DeliverMessage(keyv, chan);
                                        }
                                        else
                                        {
                                            Core.irc.Queue.DeliverMessage(User + ": " + keyv, chan);
                                        }
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
                if (Module.GetConfig(chan, "Infobot.auto-complete", false))
                {
                    if (infobot != null)
                    {
                        List<string> results = new List<string>();
                        lock (infobot)
                        {
                            foreach (InfobotKey f in infobot.Keys)
                            {
                                if (!results.Contains(f.Key) && f.Key.StartsWith(p[0]))
                                {
                                    results.Add(f.Key);
                                }
                            }
                            foreach (InfobotAlias f in infobot.Alias)
                            {
                                if (!results.Contains(f.Key) && f.Key.StartsWith(p[0]))
                                {
                                    results.Add(f.Key);
                                }
                            }
                        }

                        if (results.Count == 1)
                        {
                            keyv = infobot.GetValue(results[0]);
                            if (keyv != "")
                            {
                                keyv = ParseInfo(keyv, p, key, _key);
                                if (User == "")
                                {
                                    Core.irc.Queue.DeliverMessage(keyv, chan.Name);
                                }
                                else
                                {
                                    Core.irc.Queue.DeliverMessage(User + ": " + keyv, chan.Name);
                                }
                                return true;
                            }
                            lock (infobot)
                            {
                                foreach (InfobotAlias alias in infobot.Alias)
                                {
                                    if (alias.Name == p[0])
                                    {
                                        keyv = infobot.GetValue(alias.Key);
                                        if (keyv != "")
                                        {
                                            keyv = ParseInfo(keyv, p, key, _key);
                                            if (User == "")
                                            {
                                                Core.irc.Queue.DeliverMessage(keyv, chan.Name);
                                            }
                                            else
                                            {
                                                Core.irc.Queue.DeliverMessage(User + ": " + keyv, chan.Name);
                                            }
                                            return true;
                                        }
                                    }
                                }
                            }
                        }

                        if (results.Count > 1)
                        {
                            if (Module.GetConfig(chan, "Infobot.Sorted", false))
                            {
                                results.Sort();
                            }
                            string x = "";
                            foreach (string ix in results)
                            {
                                x += ix + ", ";
                            }
                            Core.irc.Queue.DeliverMessage(messages.Localize("infobot-c-e", chan.Language, new List<string>() { x }), chan);
                            return true;
                        }
                    }
                }

                if (Module.GetConfig(chan, "Infobot.Help", false) && infobot != null)
                {
                    List<string> Sugg = new List<string>();
                    p[0] = p[0].ToLower();
                    lock (infobot)
                    {
                        foreach (InfobotKey f in infobot.Keys)
                        {
                            if (!Sugg.Contains(f.Key) && (f.Text.Contains(p[0]) || f.Key.ToLower().Contains(p[0])))
                            {
                                Sugg.Add(f.Key);
                            }
                        }
                    }

                    if (Sugg.Count > 0)
                    {
                        string x = "";
                        if (Module.GetConfig(chan, "Infobot.Sorted", false))
                        {
                            Sugg.Sort();
                        }
                        foreach (string a in Sugg)
                        {
                            x += "!" + a + ", ";
                        }
                        Core.irc.Queue.DeliverMessage(messages.Localize("infobot-help", chan.Language, new List<string>() { x }), chan.Name);
                        return true;
                    }
                }
            }
            catch (Exception b)
            {
                Parent.HandleException(b);
            }
            return true;
        }

        private void StartSearch()
        {
            Regex value = new Regex(search_key, RegexOptions.Compiled);
            Channel _channel = Core.GetChannel(pChannel.Name);
            string results = "";
            int count = 0;
            lock (this)
            {
                foreach (InfobotKey data in Keys)
                {
                    if (data.Key == search_key || value.Match(data.Text).Success)
                    {
                        count++;
                        results = results + data.Key + ", ";
                    }
                }
            }
            if (results == "")
            {
                Core.irc.Queue.DeliverMessage(messages.Localize("ResultsWereNotFound", ReplyChan.Language), ReplyChan.Name);
            }
            else
            {
                Core.irc.Queue.DeliverMessage(messages.Localize("Results", _channel.Language, new List<string> { count.ToString() }) + results, ReplyChan.Name);
            }
            RegularModule.running = false;
        }

        /// <summary>
        /// Search
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="Chan"></param>
        public void RSearch(string key, Channel Chan)
        {
            if (!key.StartsWith("@regsearch"))
            {
                return;
            }
            if (!misc.IsValidRegex(key))
            {
                Core.irc.Queue.DeliverMessage(messages.Localize("Error1", Chan.Language), Chan.Name);
                return;
            }
            if (key.Length < 11)
            {
                Core.irc.Queue.DeliverMessage(messages.Localize("Search1", Chan.Language), Chan.Name);
                return;
            }
            Channel data = RetrieveMasterDBChannel(Chan);
            bool Allowed = (data != null);
            if (!Allowed)
            {
                Core.irc.Queue.DeliverMessage(messages.Localize("db7", Chan.Language), Chan.Name);
                return;
            }
            Infobot infobot = (Infobot)data.RetrieveObject("Infobot");
            if (infobot == null)
            {
                Syslog.Log("Unable to perform regsearch because the Infobot doesn't exist in " + Chan.Name, true);
                return;
            }
            infobot.search_key = key.Substring(11);
            RegularModule.running = true;
            ReplyChan = Chan;
            tSearch = new Thread(infobot.StartSearch);
            tSearch.Name = "Module:Infobot/Search";
            tSearch.Start();
            int check = 1;
            while (RegularModule.running)
            {
                check++;
                Thread.Sleep(100);
                if (check > 8)
                {
                    tSearch.Abort();
                    Core.irc.Queue.DeliverMessage(messages.Localize("Error2", Chan.Language), Chan.Name);
                    RegularModule.running = false;
                    return;
                }
            }
        }

        public void Find(string key, Channel Chan)
        {
            if (Chan == null)
            {
                return;
            }
            if (!key.StartsWith("@search"))
            {
                return;
            }
            Channel data = RetrieveMasterDBChannel(Chan);
            bool Allowed = (data != null);
            if (!Allowed)
            {
                Core.irc.Queue.DeliverMessage(messages.Localize("db7", Chan.Language), Chan.Name);
                return;
            }
            if (key.Length < 9)
            {
                Core.irc.Queue.DeliverMessage(messages.Localize("Error1", Chan.Language), Chan.Name);
                return;
            }
            key = key.Substring(8);
            int count = 0;
            Infobot infobot = (Infobot)data.RetrieveObject("Infobot");
            if (infobot == null)
            {
                Syslog.Log("Unable to perform regsearch because the Infobot doesn't exist in " + Chan.Name, true);
                return;
            }
            string results = "";
            lock (infobot)
            {
                foreach (InfobotKey Data in infobot.Keys)
                {
                    if (Data.Key == key || Data.Text.Contains(key))
                    {
                        results = results + Data.Key + ", ";
                        count++;
                    }
                }
            }
            if (results == "")
            {
                Core.irc.Queue.DeliverMessage(messages.Localize("ResultsWereNotFound", Chan.Language), Chan.Name);
            }
            else
            {
                Core.irc.Queue.DeliverMessage(messages.Localize("Results", Chan.Language, new List<string> { count.ToString() }) + results, Chan.Name);
            }
        }

        /// <summary>
        /// Retrieves the master DB channel
        /// </summary>
        /// <returns>
        /// The master DB channel.
        /// </returns>
        /// <param name='chan'>
        /// Chan.
        /// </param>
        private Channel RetrieveMasterDBChannel(Channel chan)
        {
            bool Allowed;
            Channel data = null;
            if (chan == null)
            {
                return chan;
            }
            if (chan.SharedDB == "local" || chan.SharedDB == "")
            {
                data = chan;
                Allowed = true;
            }
            else
            {
                Allowed = Linkable(Core.GetChannel(chan.SharedDB), chan);
                if (Allowed != false)
                {
                    data = Core.GetChannel(chan.SharedDB);
                }
                if (data == null)
                {
                    Allowed = false;
                }
            }
            if (Allowed)
            {
                return data;
            }
            return null;
        }

        public void SetRaw(string key, string user, Channel chan)
        {
            InfobotKey Key = GetKey(key, Sensitive);
            if (Key == null)
            {
                Core.irc.Queue.DeliverMessage("There is no such a key, " + user, chan.Name);
                return;
            }
            Key.Raw = true;
            Core.irc.Queue.DeliverMessage("This key will be displayed with no extra styling, variables and will ignore all symbols", chan.Name);
            stored = false;
        }

        public void UnsetRaw(string key, string user, Channel chan)
        {
            InfobotKey Key = GetKey(key, Sensitive);
            if (Key == null)
            {
                Core.irc.Queue.DeliverMessage("There is no such a key, " + user, chan.Name);
                return;
            }
            Key.Raw = false;
            Core.irc.Queue.DeliverMessage("This key will be displayed normally", chan.Name);
            stored = false;
        }

        /// <summary>
        /// Save a new key
        /// </summary>
        /// <param name="Text">Text</param>
        /// <param name="key">Key</param>
        /// <param name="user">User who created it</param>
        public void SetKey(string Text, string key, string user, Channel chan)
        {
            lock (this)
            {
                try
                {
                    if (KeyExists(key, Sensitive))
                    {
                        if (!chan.SuppressWarnings)
                        {
                            Core.irc.Queue.DeliverMessage(messages.Localize("Error3", chan.Language), chan);
                        }
                        return;
                    }
                    Keys.Add(new InfobotKey(key, Text, user, "false"));
                    Core.irc.Queue.DeliverMessage(messages.Localize("infobot6", chan.Language), chan);
                    Infobot infobot = (Infobot)pChannel.RetrieveObject("Infobot");
                    if (infobot == null)
                    {
                        Syslog.Log("Unable to save the key because the Infobot doesn't exist in " + pChannel.Name, true);
                        return;
                    }
                    infobot.stored = false;
                }
                catch (Exception b)
                {
                    Core.HandleException(b, "infobot");
                }
            }
        }

        public void SnapshotStart()
        {
            try
            {
                while (!this.stored)
                {
                    Thread.Sleep(100);
                }
                lock (this)
                {
                    DateTime creationdate = DateTime.Now;
                    Syslog.Log("Creating snapshot " + temporary_data);
                    File.Copy(datafile_xml, temporary_data);
                    Core.irc.Queue.DeliverMessage("Snapshot " + temporary_data + " was created for current database as of " + creationdate.ToString(), pChannel);
                }
            }
            catch (Exception fail)
            {
                Syslog.Log("Unable to create a snapshot for " + pChannel.Name, true);
                Core.HandleException(fail, "infobot");
            }
        }

        public void RecoverStart()
        {
            try
            {
                while (!this.stored)
                {
                    Thread.Sleep(100);
                }
                lock (this)
                {
                    Syslog.Log("Recovering snapshot " + temporary_data);
                    File.Copy(temporary_data, datafile_xml, true);
                    this.Keys.Clear();
                    this.Alias.Clear();
                    Parent.Log("Loading snapshot of " + pChannel.Name);
                    LoadData();
                    Core.irc.Queue.DeliverMessage("Snapshot " + temporary_data + " was loaded and previous database was permanently deleted", pChannel);
                }
            }
            catch (Exception fail)
            {
                Parent.Log("Unable to recover a snapshot for " + pChannel.Name + " the db is likely broken now", true);
                Parent.HandleException(fail);
            }
        }

        public bool IsValid(string name)
        {
            if (name == "")
            {
                return false;
            }
            foreach (char i in name)
            {
                if (i == '\0')
                {
                    continue;
                }
                if (((int)i) < 48)
                {
                    return false;
                }
                if (((int)i) > 122)
                {
                    return false;
                }
                if (((int)i) > 90)
                {
                    if (((int)i) < 97)
                    {
                        return false;
                    }
                }
                if (((int)i) > 57)
                {
                    if (((int)i) < 65)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public void RecoverSnapshot(Channel chan, string name)
        {
            try
            {
                lock (this)
                {
                    if (!IsValid(name))
                    {
                        Core.irc.Queue.DeliverMessage("This is not a valid name for tsnapsho, you can only use a-zA-Z and 0-9 chars", chan.Name);
                        return;
                    }
                    if (SnapshotManager != null)
                    {
                        if (SnapshotManager.ThreadState == ThreadState.Running)
                        {
                            Core.irc.Queue.DeliverMessage("There is already another snapshot operation running for this channel", chan.Name);
                            return;
                        }
                    }
                    string datafile = RegularModule.SnapshotsDirectory + Path.DirectorySeparatorChar + pChannel.Name + Path.DirectorySeparatorChar + name;
                    if (!File.Exists(datafile))
                    {
                        Core.irc.Queue.DeliverMessage("The requested datafile " + name + " was not found", chan.Name, IRC.priority.low);
                        return;
                    }

                    SnapshotManager = new Thread(RecoverStart);
                    temporary_data = datafile;
                    SnapshotManager.Name = "Module:Infobot/Snapshot";
                    Core.ThreadManager.RegisterThread(SnapshotManager);
                    SnapshotManager.Start();
                    RegularModule.SetConfig(chan, "HTML.Update", true);
                }
            }
            catch (Exception fail)
            {
                Parent.HandleException(fail);
            }
        }

        public void CreateSnapshot(Channel chan, string name)
        {
            try
            {
                if (!IsValid(name))
                {
                    Core.irc.Queue.DeliverMessage("This is not a valid name for snapshot, you can only use a-zA-Z and 0-9 chars", chan.Name);
                    return;
                }
                if (SnapshotManager != null)
                {
                    if (SnapshotManager.ThreadState == ThreadState.Running)
                    {
                        Core.irc.Queue.DeliverMessage("There is already another snapshot operation running for this channel", chan.Name);
                        return;
                    }
                }
                string datafile = RegularModule.SnapshotsDirectory + Path.DirectorySeparatorChar + pChannel.Name + Path.DirectorySeparatorChar + name;
                if (File.Exists(datafile))
                {
                    Core.irc.Queue.DeliverMessage("The requested snapshot " + name + " already exist", chan.Name, IRC.priority.low);
                    return;
                }
                SnapshotManager = new Thread(SnapshotStart);
                temporary_data = datafile;
                SnapshotManager.Name = "Snapshot";
                SnapshotManager.Start();
            }
            catch (Exception fail)
            {
                Parent.HandleException(fail);
            }
        }

        /// <summary>
        /// Alias
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="al">Alias</param>
        /// <param name="user">User</param>
        public void aliasKey(string key, string al, string user, Channel chan, bool enforced = false)
        {
            lock (this)
            {
                foreach (InfobotAlias stakey in Alias)
                {
                    if (stakey.Name == al)
                    {
                        if (!chan.SuppressWarnings)
                        {
                            Core.irc.Queue.DeliverMessage(messages.Localize("infobot7", chan.Language), chan.Name);
                        }
                        return;
                    }
                }
                if (!KeyExists(key))
                {
                    if (!enforced)
                    {
                        if (AliasExists(key))
                        {
                            Core.irc.Queue.DeliverMessage("Unable to create alias for " + key + " because the target is alias, but not a key, if you really want to create this broken alias do !" + al + " force-alias " + key, chan.Name);
                            return;
                        }
                        Core.irc.Queue.DeliverMessage("Unable to create alias for " + key + " because there is no such key, if you really want to create this broken alias do !" + al + " force-alias " + key, chan.Name);
                        return;
                    }
                }
                Alias.Add(new InfobotAlias(al, key));
            }
            Core.irc.Queue.DeliverMessage(messages.Localize("infobot8", chan.Language), chan.Name);
            stored = false;
        }

        public void rmKey(string key, string user, Channel _ch)
        {
            lock (this)
            {
                foreach (InfobotKey keys in Keys)
                {
                    if (Sensitive)
                    {
                        if (keys.Key == key)
                        {
                            Keys.Remove(keys);
                            Core.irc.Queue.DeliverMessage(messages.Localize("infobot9", _ch.Language) + key, _ch.Name);
                            stored = false;
                            return;
                        }
                    }
                    else
                    {
                        if (keys.Key.ToLower() == key.ToLower())
                        {
                            Keys.Remove(keys);
                            Core.irc.Queue.DeliverMessage(messages.Localize("infobot9", _ch.Language) + key, _ch.Name);
                            stored = false;
                            return;
                        }
                    }
                }
            }
            Core.irc.Queue.DeliverMessage(messages.Localize("infobot10", _ch.Language), _ch.Name);
        }
    }
}
