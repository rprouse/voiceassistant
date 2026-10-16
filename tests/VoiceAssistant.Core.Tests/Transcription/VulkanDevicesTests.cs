using VoiceAssistant.Core.Transcription;

namespace VoiceAssistant.Core.Tests.Transcription
{
    public class VulkanDevicesTests
    {
        // Captured from whisper.cpp's log on a laptop with Intel and NVIDIA graphics.
        private const string Intel = "ggml_vulkan: 0 = Intel(R) UHD Graphics (Intel Corporation) | uma: 1 | fp16: 1 | bf16: 0 | warp size: 32 | shared memory: 32768 | int dot: 1 | matrix cores: none";
        private const string Nvidia = "ggml_vulkan: 1 = NVIDIA GeForce RTX 3050 Ti Laptop GPU (NVIDIA) | uma: 0 | fp16: 1 | bf16: 1 | warp size: 32 | shared memory: 49152 | int dot: 1 | matrix cores: NV_coopmat2";

        [Test]
        public void ParsesIntegratedGpuWithParenthesesInItsName()
        {
            VulkanDevices.TryParse(Intel).ShouldBe(new VulkanDevice(0, "Intel(R) UHD Graphics", IsIntegrated: true));
        }

        [Test]
        public void ParsesDedicatedGpu()
        {
            VulkanDevices.TryParse(Nvidia).ShouldBe(new VulkanDevice(1, "NVIDIA GeForce RTX 3050 Ti Laptop GPU", IsIntegrated: false));
        }

        [TestCase("ggml_vulkan: Found 2 Vulkan devices:")]
        [TestCase("whisper_init_with_params_no_state: gpu_device = 1")]
        [TestCase("")]
        public void IgnoresOtherLines(string line)
        {
            VulkanDevices.TryParse(line).ShouldBeNull();
        }

        [Test]
        public void DisplayNameSaysWhichKindOfGpuItIs()
        {
            VulkanDevices.TryParse(Nvidia)!.DisplayName.ShouldBe("1 · NVIDIA GeForce RTX 3050 Ti Laptop GPU (dedicated)");
        }

        [Test]
        public void PreferredIsTheFirstDedicatedGpu()
        {
            VulkanDevices.Observe(Intel);
            VulkanDevices.Observe(Nvidia);

            VulkanDevices.Preferred.ShouldNotBeNull().Index.ShouldBe(1);
            VulkanDevices.Get(0).ShouldNotBeNull().IsIntegrated.ShouldBeTrue();
        }
    }
}
