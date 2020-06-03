using System;

namespace NineChronicles.Standalone.Exceptions
{
    public class PeerInvalidException: Exception
    {
        public PeerInvalidException()
        {
        }

        public PeerInvalidException(string message)
            : base(message)
        {
        }

        public PeerInvalidException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
