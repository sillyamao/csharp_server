﻿namespace GameServer
{
    public abstract class Command
    {
        public virtual void Run(string[] args)
        {
            Logger.LogWarning("Unimplemented command.");
        }
    }
}
