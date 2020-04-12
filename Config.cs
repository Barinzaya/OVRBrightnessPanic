namespace OVRBrightnessPanic
{
    public class Config
    {
        public float ActivateFactor { get; set; } = 0.5f;
        public float ResetRate { get; set; } = 0.25f;

        public float UpdateFrequency { get; set; } = 60f;

        public string ActivateSound { get; set; } = "activate.wav";
        public string ResetSound { get; set; } = "reset.wav";

        public AutoBrightnessConfig Auto { get; set; } = new AutoBrightnessConfig();
    }

    public class AutoBrightnessConfig
    {
        public bool Enabled { get; set; } = true;
        public float BrightnessFrequency { get; set; } = 10f;

        public float DynamicMinBrightness { get; set; } = 0.25f;
        public float DynamicMaxRate { get; set; } = 1.0f;

        public float RecoverMargin { get; set; } = 0.05f;
        public float RecoverRate { get; set; } = 0.25f;

        public float StaticMaxBrightness { get; set; } = 0.5f;
    }
}
