//This program is free software: you can redistribute it and/or modify
//it under the terms of the GNU General Public License as published by
//the Free Software Foundation, either version 3 of the License, or
//(at your option) any later version.

//This program is distributed in the hope that it will be useful,
//but WITHOUT ANY WARRANTY; without even the implied warranty of
//MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//GNU General Public License for more details.

// Created by Petr Bena

using System;
using System.Collections.Generic;
using System.Threading;
using System.Text.RegularExpressions;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Soap;
using System.IO;

namespace wmib
{
    public class RegularModule : Module
    {
        private List<Infobot.InfoItem> jobs = new List<Infobot.InfoItem>();
        public static bool running;
        private bool Unwritable;
        public static bool Snapshots = true;
        public readonly static string SnapshotsDirectory = "snapshots";
        private infobot_writer writer = null;

        public override bool Hook_OnUnload()
        {
            bool success = true;
            if (writer != null)
            {
                writer.Exit();
                writer = null;
            }
            lock (Configuration.Channels)
            {
                foreach (Channel channel in Configuration.Channels)
                {
                    if (!channel.UnregisterObject("Infobot"))
                    {
                        success = false;
                    }
                }
            }
            if (!success)
            {
                Syslog.Log("Failed to unregister infobot objects in some channels", true);
            }
            return success;
        }

        public string getDB(ref Channel chan)
        {
            return Module.GetConfig(chan, "Infobot.Keydb", (string)Variables.ConfigurationDirectory + Path.DirectorySeparatorChar + chan.Name + ".db");
        }

        public override void Hook_ChannelDrop(Channel chan)
        {
            try
            {
                if (Directory.Exists(SnapshotsDirectory + Path.DirectorySeparatorChar + chan.Name))
                {
                    Syslog.Log("Removing snapshots for " + chan.Name);
                    Directory.Delete(SnapshotsDirectory + Path.DirectorySeparatorChar + chan.Name, true);
                }
            }
            catch (Exception fail)
            {
                Core.HandleException(fail, "infobot");
            }
        }

        public override void Hook_Channel(Channel channel)
        {
            Syslog.Log("Loading " + channel.Name);
            if (channel == null)
            {
                Syslog.Log("NULL");
            }
            if (Snapshots)
            {
                try
                {
                    if (Directory.Exists(SnapshotsDirectory + Path.DirectorySeparatorChar + channel.Name) == false)
                    {
                        Syslog.Log("Creating directory for infobot for " + channel.Name);
                        Directory.CreateDirectory(SnapshotsDirectory + Path.DirectorySeparatorChar + channel.Name);
                    }
                }
                catch (Exception fail)
                {
                    Core.HandleException(fail, "infobot");
                }
            }
            if (channel.RetrieveObject("Infobot") == null)
            {
                // sensitivity
                bool cs = Module.GetConfig(channel, "Infobot.Case", true);
                channel.RegisterObject(new Infobot(getDB(ref channel), channel, this, cs), "Infobot");
            }
        }

        public override bool Hook_OnRegister()
        {
            bool success = true;
            DebugLog("Registering channels");
            try
            {
                if (!Directory.Exists(SnapshotsDirectory))
                {
                    Syslog.Log("Creating snapshot directory for infobot");
                    Directory.CreateDirectory(SnapshotsDirectory);
                }
            }
            catch (Exception fail)
            {
                Snapshots = false;
                Core.HandleException(fail, "infobot");
            }
            writer = new infobot_writer();
            writer.Construct();
            ExtensionHandler.InitialiseMod(writer);
            lock (Configuration.Channels)
            {
                foreach (Channel channel in Configuration.Channels)
                {
                    Channel curr = channel;
                    bool cs = Module.GetConfig(curr, "Infobot.Case", true);
                    if (!channel.RegisterObject(new Infobot(getDB(ref curr), channel, this, cs), "Infobot"))
                    {
                        success = false;
                    }
                    if (Snapshots)
                    {
                        try
                        {
                            if (Directory.Exists(SnapshotsDirectory + Path.DirectorySeparatorChar + channel.Name) == false)
                            {
                                Syslog.Log("Creating directory for infobot for " + channel.Name);
                                Directory.CreateDirectory(SnapshotsDirectory + Path.DirectorySeparatorChar + channel.Name);
                            }
                        }
                        catch (Exception fail)
                        {
                            Core.HandleException(fail, "infobot");
                        }
                    }
                }
            }
            if (!success)
            {
                Syslog.Log("Failed to register infobot objects in some channels", true);
            }
            return success;
        }

        public override string Extension_DumpHtml(Channel channel)
        {
            string HTML = "";
            Infobot info = (Infobot)channel.RetrieveObject("Infobot");
            if (info != null)
            {
                HTML += "\n<table border=1 class=\"infobot\" width=100%>\n<tr><th width=10%>Key</th><th>Value</th></tr>\n";
                List<Infobot.InfobotKey> list = new List<Infobot.InfobotKey>();
                lock (info)
                {
                    if (Module.GetConfig(channel, "Infobot.Sorted", false) != false)
                    {
                        list = info.SortedItem();
                    }
                    else
                    {
                        list.AddRange(info.Keys);
                    }
                }
                if (info.Keys.Count > 0)
                {
                    foreach (Infobot.InfobotKey Key in list)
                    {
                        HTML += Core.HTML.AddKey(Key.Key, Key.Text);
                    }
                }
                HTML += "</table>\n";
                HTML += "<h4>Aliases</h4>\n<table class=\"infobot\" border=1 width=100%>\n";
                lock (info)
                {
                    foreach (Infobot.InfobotAlias data in info.Alias)
                    {
                        HTML += Core.HTML.AddLink(data.Name, data.Key);
                    }
                }
                HTML += "</table><br>\n";
            }
            return HTML;
        }

        public override void Hook_PRIV(Channel channel, User invoker, string message)
        {
            // "\uff01" is the full-width version of "!".
            if ((message.StartsWith("!") || message.StartsWith("\uff01")) && GetConfig(channel, "Infobot.Enabled", true))
            {
                while (Unwritable)
                {
                    Thread.Sleep(10);
                }
                Unwritable = true;
                Infobot.InfoItem item = new Infobot.InfoItem();
                item._Channel = channel;
                item.Name = "!" + message.Substring(1); // Normalizing "!".
                item.User = invoker.Nick;
                item.Host = invoker.Host;
                jobs.Add(item);
                Unwritable = false;
            }

            Infobot infobot = null;

            if (message.StartsWith(Configuration.System.CommandPrefix))
            {
                infobot = (Infobot)channel.RetrieveObject("Infobot");
                if (infobot == null)
                {
                    Syslog.Log("Object Infobot in " + channel.Name + " doesn't exist", true);
                }
                if (GetConfig(channel, "Infobot.Enabled", true))
                {
                    if (infobot != null)
                    {
                        infobot.Find(message, channel);
                        infobot.RSearch(message, channel);
                    }
                }
            }

            if (Snapshots)
            {
                if (message.StartsWith(Configuration.System.CommandPrefix + "infobot-recovery "))
                {
                    if (channel.SystemUsers.IsApproved(invoker, "admin"))
                    {
                        string name = message.Substring("@infobot-recovery ".Length);
                        if (!GetConfig(channel, "Infobot.Enabled", true))
                        {
                            Core.irc.Queue.DeliverMessage("Infobot is not enabled in this channel", channel, IRC.priority.low);
                            return;
                        }
                        if (infobot != null)
                        {
                            infobot.RecoverSnapshot(channel, name);
                        }
                        return;
                    }
                    if (!channel.SuppressWarnings)
                    {
                        Core.irc.Queue.DeliverMessage(messages.Localize("PermissionDenied", channel.Language), channel, IRC.priority.low);
                    }
                    return;
                }

                if (message.StartsWith(Configuration.System.CommandPrefix + "infobot-snapshot "))
                {
                    if (channel.SystemUsers.IsApproved(invoker, "admin"))
                    {
                        string name = message.Substring("@infobot-snapshot ".Length);
                        if (!GetConfig(channel, "Infobot.Enabled", true))
                        {
                            Core.irc.Queue.DeliverMessage("Infobot is not enabled in this channel", channel, IRC.priority.low);
                            return;
                        }
                        if (infobot != null)
                        {
                            infobot.CreateSnapshot(channel, name);
                        }
                        return;
                    }
                    if (!channel.SuppressWarnings)
                    {
                        Core.irc.Queue.DeliverMessage(messages.Localize("PermissionDenied", channel.Language), channel, IRC.priority.low);
                    }
                    return;
                }

                if (message.StartsWith(Configuration.System.CommandPrefix + "infobot-set-raw "))
                {
                    if (channel.SystemUsers.IsApproved(invoker, "trust"))
                    {
                        string name = message.Substring("@infobot-set-raw ".Length);
                        if (!GetConfig(channel, "Infobot.Enabled", true))
                        {
                            Core.irc.Queue.DeliverMessage("Infobot is not enabled in this channel", channel, IRC.priority.low);
                            return;
                        }
                        if (infobot != null)
                        {
                            infobot.SetRaw(name, invoker.Nick, channel);
                            return;
                        }
                    }
                    if (!channel.SuppressWarnings)
                    {
                        Core.irc.Queue.DeliverMessage(messages.Localize("PermissionDenied", channel.Language), channel, IRC.priority.low);
                    }
                    return;
                }

                if (message.StartsWith(Configuration.System.CommandPrefix + "infobot-unset-raw "))
                {
                    if (channel.SystemUsers.IsApproved(invoker, "trust"))
                    {
                        string name = message.Substring("@infobot-unset-raw ".Length);
                        if (!GetConfig(channel, "Infobot.Enabled", true))
                        {
                            Core.irc.Queue.DeliverMessage("Infobot is not enabled in this channel", channel, IRC.priority.low);
                            return;
                        }
                        if (infobot != null)
                        {
                            infobot.UnsetRaw(name, invoker.Nick, channel);
                            return;
                        }
                    }
                    if (!channel.SuppressWarnings)
                    {
                        Core.irc.Queue.DeliverMessage(messages.Localize("PermissionDenied", channel.Language), channel, IRC.priority.low);
                    }
                    return;
                }

                if (message.StartsWith(Configuration.System.CommandPrefix + "infobot-snapshot-rm "))
                {
                    if (channel.SystemUsers.IsApproved(invoker, "admin"))
                    {
                        string name = message.Substring("@infobot-snapshot-rm ".Length);
                        name.Replace(".", "");
                        name.Replace("/", "");
                        name.Replace("\\", "");
                        name.Replace("*", "");
                        name.Replace("?", "");
                        if (name == "")
                        {
                            Core.irc.Queue.DeliverMessage("You should specify a file name", channel, IRC.priority.normal);
                            return;
                        }
                        if (!File.Exists(SnapshotsDirectory + Path.DirectorySeparatorChar + channel.Name + Path.DirectorySeparatorChar + name))
                        {
                            Core.irc.Queue.DeliverMessage("File not found", channel, IRC.priority.normal);
                            return;
                        }
                        File.Delete(SnapshotsDirectory + Path.DirectorySeparatorChar + channel.Name + Path.DirectorySeparatorChar + name);
                        Core.irc.Queue.DeliverMessage("Requested file was removed", channel, IRC.priority.normal);
                        return;
                    }
                    if (!channel.SuppressWarnings)
                    {
                        Core.irc.Queue.DeliverMessage(messages.Localize("PermissionDenied", channel.Language), channel, IRC.priority.low);
                    }
                    return;
                }

                if (message == Configuration.System.CommandPrefix + "infobot-snapshot-ls")
                {
                    string files = "";
                    DirectoryInfo di = new DirectoryInfo(SnapshotsDirectory + Path.DirectorySeparatorChar + channel.Name);
                    FileInfo[] rgFiles = di.GetFiles("*");
                    int curr = 0;
                    int displaying = 0;
                    foreach (FileInfo fi in rgFiles)
                    {
                        curr++;
                        if (files.Length < 200)
                        {
                            files += fi.Name + " ";
                            displaying++;
                        }
                    }
                    string response = "";
                    if (curr == displaying)
                    {
                        response = "There are " + displaying.ToString() + " files: " + files;
                    }
                    else
                    {
                        response = "There are " + curr.ToString() + " files, but displaying only " + displaying.ToString() + " of them: " + files;
                    }
                    if (curr == 0)
                    {
                        response = "There is no snapshot so far, create one!:)";
                    }
                    Core.irc.Queue.DeliverMessage(response, channel.Name, IRC.priority.normal);
                    return;
                }
            }

            if (message.StartsWith(Configuration.System.CommandPrefix + "infobot-share-trust+ "))
            {
                if (channel.SystemUsers.IsApproved(invoker, "admin"))
                {
                    if (channel.SharedDB != "local")
                    {
                        Core.irc.Queue.DeliverMessage(messages.Localize("infobot16", channel.Language), channel);
                        return;
                    }
                    if (channel.SharedDB != "local" && channel.SharedDB != "")
                    {
                        Core.irc.Queue.DeliverMessage(messages.Localize("infobot15", channel.Language), channel);
                        return;
                    }
                    else
                    {
                        if (message.Length <= "@infobot-share-trust+ ".Length)
                        {
                            Core.irc.Queue.DeliverMessage(messages.Localize("db6", channel.Language), channel.Name);
                            return;
                        }
                        string name = message.Substring("@infobot-share-trust+ ".Length);
                        Channel guest = Core.GetChannel(name);
                        if (guest == null)
                        {
                            Core.irc.Queue.DeliverMessage(messages.Localize("db8", channel.Language), channel.Name);
                            return;
                        }
                        if (channel.SharedLinkedChan.Contains(guest))
                        {
                            Core.irc.Queue.DeliverMessage(messages.Localize("db14", channel.Language), channel.Name);
                            return;
                        }
                        Core.irc.Queue.DeliverMessage(messages.Localize("db1", channel.Language, new List<string> { name }), channel.Name);
                        lock (channel.SharedLinkedChan)
                        {
                            channel.SharedLinkedChan.Add(guest);
                        }
                        channel.SaveConfig();
                        return;
                    }
                }
                if (!channel.SuppressWarnings)
                {
                    Core.irc.Queue.DeliverMessage(messages.Localize("PermissionDenied", channel.Language), channel.Name, IRC.priority.low);
                }
                return;
            }

            if (message.StartsWith(Configuration.System.CommandPrefix + "infobot-ignore- "))
            {
                if (channel.SystemUsers.IsApproved(invoker, "trust"))
                {
                    string item = message.Substring("@infobot-ignore+ ".Length);
                    if (item != "")
                    {
                        if (!channel.Infobot_IgnoredNames.Contains(item))
                        {
                            Core.irc.Queue.DeliverMessage(messages.Localize("infobot-ignore-found", channel.Language, new List<string> { item }), channel);
                            return;
                        }
                        channel.Infobot_IgnoredNames.Remove(item);
                        Core.irc.Queue.DeliverMessage(messages.Localize("infobot-ignore-rm", channel.Language, new List<string> { item }), channel);
                        channel.SaveConfig();
                        return;
                    }
                }
                else
                {
                    if (!channel.SuppressWarnings)
                    {
                        Core.irc.Queue.DeliverMessage(messages.Localize("PermissionDenied", channel.Language), channel, IRC.priority.low);
                    }
                }
            }

            if (message.StartsWith(Configuration.System.CommandPrefix + "infobot-ignore+ "))
            {
                if (channel.SystemUsers.IsApproved(invoker, "trust"))
                {
                    string item = message.Substring("@infobot-ignore+ ".Length);
                    if (item != "")
                    {
                        if (channel.Infobot_IgnoredNames.Contains(item))
                        {
                            Core.irc.Queue.DeliverMessage(messages.Localize("infobot-ignore-exist", channel.Language, new List<string> { item }), channel);
                            return;
                        }
                        channel.Infobot_IgnoredNames.Add(item);
                        Core.irc.Queue.DeliverMessage(messages.Localize("infobot-ignore-ok", channel.Language, new List<string> { item }), channel);
                        channel.SaveConfig();
                        return;
                    }
                }
                else
                {
                    if (!channel.SuppressWarnings)
                    {
                        Core.irc.Queue.DeliverMessage(messages.Localize("PermissionDenied", channel.Language), channel, IRC.priority.low);
                    }
                }
            }

            if (message == Configuration.System.CommandPrefix + "infobot-off")
            {
                if (channel.SystemUsers.IsApproved(invoker, "admin"))
                {
                    if (!GetConfig(channel, "Infobot.Enabled", true))
                    {
                        Core.irc.Queue.DeliverMessage(messages.Localize("infobot1", channel.Language), channel);
                        return;
                    }
                    else
                    {
                        Core.irc.Queue.DeliverMessage(messages.Localize("infobot2", channel.Language), channel, IRC.priority.high);
                        SetConfig(channel, "Infobot.Enabled", false);
                        channel.SaveConfig();
                        return;
                    }
                }
                if (!channel.SuppressWarnings)
                {
                    Core.irc.Queue.DeliverMessage(messages.Localize("PermissionDenied", channel.Language), channel, IRC.priority.low);
                }
                return;
            }

            if (message.StartsWith(Configuration.System.CommandPrefix + "infobot-share-trust- "))
            {
                if (channel.SystemUsers.IsApproved(invoker, "admin"))
                {
                    if (channel.SharedDB != "local")
                    {
                        Core.irc.Queue.DeliverMessage(messages.Localize("infobot16", channel.Language), channel);
                        return;
                    }
                    else
                    {
                        if (message.Length <= "@infobot-share-trust+ ".Length)
                        {
                            Core.irc.Queue.DeliverMessage(messages.Localize("db6", channel.Language), channel);
                            return;
                        }
                        string name = message.Substring("@infobot-share-trust- ".Length);
                        Channel target = Core.GetChannel(name);
                        if (target == null)
                        {
                            Core.irc.Queue.DeliverMessage(messages.Localize("db8", channel.Language), channel);
                            return;
                        }
                        if (channel.SharedLinkedChan.Contains(target))
                        {
                            channel.SharedLinkedChan.Remove(target);
                            Core.irc.Queue.DeliverMessage(messages.Localize("db2", channel.Language, new List<string> { name }), channel);
                            channel.SaveConfig();
                            return;
                        }
                        Core.irc.Queue.DeliverMessage(messages.Localize("db4", channel.Language), channel);
                        return;
                    }
                }
                if (!channel.SuppressWarnings)
                {
                    Core.irc.Queue.DeliverMessage(messages.Localize("PermissionDenied", channel.Language), channel, IRC.priority.low);
                }
                return;
            }

            if (message.StartsWith(Configuration.System.CommandPrefix + "infobot-detail "))
            {
                if ((message.Length) <= "@infobot-detail ".Length)
                {
                    Core.irc.Queue.DeliverMessage(messages.Localize("db6", channel.Language), channel);
                    return;
                }
                if (GetConfig(channel, "Infobot.Enabled", true))
                {
                    if (channel.SharedDB == "local" || channel.SharedDB == "")
                    {
                        if (infobot != null)
                        {
                            infobot.InfobotDetail(message.Substring(16), channel);
                        }
                        return;
                    }
                    if (channel.SharedDB != "")
                    {
                        Channel db = Core.GetChannel(channel.SharedDB);
                        if (db == null)
                        {
                            Core.irc.Queue.DeliverMessage("Error, null pointer to shared channel", channel, IRC.priority.low);
                            return;
                        }
                        if (infobot != null)
                        {
                            infobot.InfobotDetail(message.Substring(16), channel);
                        }
                        return;
                    }
                    return;
                }
                Core.irc.Queue.DeliverMessage("Infobot is not enabled on this channel", channel, IRC.priority.low);
                return;
            }

            if (message.StartsWith(Configuration.System.CommandPrefix + "infobot-link "))
            {
                if (channel.SystemUsers.IsApproved(invoker, "admin"))
                {
                    if (channel.SharedDB == "local")
                    {
                        Core.irc.Queue.DeliverMessage(messages.Localize("infobot17", channel.Language), channel);
                        return;
                    }
                    if (channel.SharedDB != "")
                    {
                        Core.irc.Queue.DeliverMessage(messages.Localize("infobot18", channel.Language, new List<string> { channel.SharedDB }), channel);
                        return;
                    }
                    if ((message.Length - 1) < "@infobot-link ".Length)
                    {
                        Core.irc.Queue.DeliverMessage(messages.Localize("db6", channel.Language), channel);
                        return;
                    }
                    string name = message.Substring("@infobot-link ".Length);
                    Channel db = Core.GetChannel(name);
                    if (db == null)
                    {
                        Core.irc.Queue.DeliverMessage(messages.Localize("db8", channel.Language), channel);
                        return;
                    }
                    if (!Infobot.Linkable(db, channel))
                    {
                        Core.irc.Queue.DeliverMessage(messages.Localize("db9", channel.Language), channel);
                        return;
                    }
                    channel.SharedDB = name.ToLower();
                    Core.irc.Queue.DeliverMessage(messages.Localize("db10", channel.Language), channel);
                    channel.SaveConfig();
                    return;
                }
                if (!channel.SuppressWarnings)
                {
                    Core.irc.Queue.DeliverMessage(messages.Localize("PermissionDenied", channel.Language), channel, IRC.priority.low);
                }
                return;
            }

            if (message == Configuration.System.CommandPrefix + "infobot-share-off")
            {
                if (channel.SystemUsers.IsApproved(invoker, "admin"))
                {
                    if (channel.SharedDB == "")
                    {
                        Core.irc.Queue.DeliverMessage(messages.Localize("infobot14", channel.Language), channel);
                        return;
                    }
                    else
                    {
                        Core.irc.Queue.DeliverMessage(messages.Localize("infobot13", channel.Language), channel);
                        lock (Configuration.Channels)
                        {
                            foreach (Channel curr in Configuration.Channels)
                            {
                                if (curr.SharedDB == channel.Name.ToLower())
                                {
                                    curr.SharedDB = "";
                                    curr.SaveConfig();
                                    Core.irc.Queue.DeliverMessage(messages.Localize("infobot19", curr.Language, new List<string> { invoker.Nick }), curr);
                                }
                            }
                        }
                        channel.SharedDB = "";
                        channel.SaveConfig();
                        return;
                    }
                }
                if (!channel.SuppressWarnings)
                {
                    Core.irc.Queue.DeliverMessage(messages.Localize("PermissionDenied", channel.Language), channel, IRC.priority.low);
                }
                return;
            }

            if (message == Configuration.System.CommandPrefix + "infobot-on")
            {
                if (channel.SystemUsers.IsApproved(invoker, "admin"))
                {
                    if (GetConfig(channel, "Infobot.Enabled", true))
                    {
                        Core.irc.Queue.DeliverMessage(messages.Localize("infobot3", channel.Language), channel);
                        return;
                    }
                    SetConfig(channel, "Infobot.Enabled", true);
                    channel.SaveConfig();
                    Core.irc.Queue.DeliverMessage(messages.Localize("infobot4", channel.Language), channel, IRC.priority.high);
                    return;
                }
                if (!channel.SuppressWarnings)
                {
                    Core.irc.Queue.DeliverMessage(messages.Localize("PermissionDenied", channel.Language), channel, IRC.priority.low);
                }
                return;
            }

            if (message == Configuration.System.CommandPrefix + "infobot-share-on")
            {
                if (channel.SystemUsers.IsApproved(invoker, "admin"))
                {
                    if (channel.SharedDB == "local")
                    {
                        Core.irc.Queue.DeliverMessage(messages.Localize("infobot11", channel.Language), channel, IRC.priority.high);
                        return;
                    }
                    if (channel.SharedDB != "local" && channel.SharedDB != "")
                    {
                        Core.irc.Queue.DeliverMessage(messages.Localize("infobot15", channel.Language), channel, IRC.priority.high);
                        return;
                    }
                    else
                    {
                        Core.irc.Queue.DeliverMessage(messages.Localize("infobot12", channel.Language), channel);
                        channel.SharedDB = "local";
                        channel.SaveConfig();
                        return;
                    }
                }
                if (!channel.SuppressWarnings)
                {
                    Core.irc.Queue.DeliverMessage(messages.Localize("PermissionDenied", channel.Language), channel, IRC.priority.low);
                }
                return;
            }
        }

        public override bool Hook_SetConfig(Channel chan, User invoker, string config, string value)
        {
            bool _temp_a;
            switch (config)
            {
                case "infobot-trim-white-space-in-name":
                    if (bool.TryParse(value, out _temp_a))
                    {
                        Module.SetConfig(chan, "Infobot.Trim-white-space-in-name", _temp_a);
                        Core.irc.Queue.DeliverMessage(messages.Localize("configuresave", chan.Language, new List<string> { value, config }), chan.Name);
                        chan.SaveConfig();
                        return true;
                    }
                    Core.irc.Queue.DeliverMessage(messages.Localize("configure-va", chan.Language, new List<string> { config, value }), chan.Name);
                    return true;
                case "infobot-auto-complete":
                    if (bool.TryParse(value, out _temp_a))
                    {
                        Module.SetConfig(chan, "Infobot.auto-complete", _temp_a);
                        Core.irc.Queue.DeliverMessage(messages.Localize("configuresave", chan.Language, new List<string> { value, config }), chan.Name);
                        chan.SaveConfig();
                        return true;
                    }
                    Core.irc.Queue.DeliverMessage(messages.Localize("configure-va", chan.Language, new List<string> { config, value }), chan.Name);
                    return true;
                case "infobot-sorted":
                    if (bool.TryParse(value, out _temp_a))
                    {
                        Module.SetConfig(chan, "Infobot.Sorted", _temp_a);
                        Core.irc.Queue.DeliverMessage(messages.Localize("configuresave", chan.Language, new List<string> { value, config }), chan.Name);
                        chan.SaveConfig();
                        return true;
                    }
                    Core.irc.Queue.DeliverMessage(messages.Localize("configure-va", chan.Language, new List<string> { config, value }), chan.Name);
                    return true;
                case "infobot-help":
                    if (bool.TryParse(value, out _temp_a))
                    {
                        Module.SetConfig(chan, "Infobot.Help", _temp_a);
                        Core.irc.Queue.DeliverMessage(messages.Localize("configuresave", chan.Language, new List<string> { value, config }), chan.Name);
                        chan.SaveConfig();
                        return true;
                    }
                    Core.irc.Queue.DeliverMessage(messages.Localize("configure-va", chan.Language, new List<string> { config, value }), chan.Name);
                    return true;
                case "infobot-case":
                    if (bool.TryParse(value, out _temp_a))
                    {
                        Module.SetConfig(chan, "Infobot.Case", _temp_a);
                        Core.irc.Queue.DeliverMessage(messages.Localize("configuresave", chan.Language, new List<string> { value, config }), chan.Name);
                        chan.SaveConfig();
                        Infobot infobot = (Infobot)chan.RetrieveObject("Infobot");
                        if (infobot != null)
                        {
                            infobot.Sensitive = _temp_a;
                        }
                        return true;
                    }
                    Core.irc.Queue.DeliverMessage(messages.Localize("configure-va", chan.Language, new List<string> { config, value }), chan.Name);
                    return true;
            }
            return false;
        }

        public override bool Construct()
        {
            Name = "Infobot Core";
            RestartOnModuleCrash = true;
            Version = "1.6.0";
            return true;
        }

        public override void Hook_ReloadConfig(Channel chan)
        {
            if (chan.ExtensionObjects.ContainsKey("Infobot"))
            {
                chan.ExtensionObjects["Infobot"] = new Infobot(getDB(ref chan), chan, this);
            }
        }

        public override void Load()
        {
            try
            {
                Unwritable = false;
                while (Core.IsRunning && IsWorking)
                {
                    if (Unwritable)
                    {
                        Thread.Sleep(200);
                    }
                    else if (jobs.Count > 0)
                    {
                        Unwritable = true;
                        List<Infobot.InfoItem> list = new List<Infobot.InfoItem>();
                        list.AddRange(jobs);
                        jobs.Clear();
                        Unwritable = false;
                        foreach (Infobot.InfoItem item in list)
                        {
                            Infobot infobot = (Infobot)item._Channel.RetrieveObject("Infobot");
                            if (infobot != null)
                            {
                                infobot.InfobotExec(item.Name, item.User, item._Channel, item.Host);
                            }
                        }
                    }
                    Thread.Sleep(200);
                }
            }
            catch (Exception b)
            {
                Unwritable = false;
                Console.WriteLine(b.InnerException);
            }
            return;
        }
    }
}
