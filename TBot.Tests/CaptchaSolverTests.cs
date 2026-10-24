using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Tbot.Services;
using Xunit;

namespace TBot.Tests {
	public class CaptchaSolverTests {
		[Fact]
		public void UnknownCaptchaDoesNotGuessAnAnswer() {
			var exception = Assert.Throws<CaptchaSolverException>(() =>
				OgameCaptchaSolver.GetCaptchaSolution(CreatePng(240, 8), CreatePng(1, 1)));

			Assert.Contains("not recognized", exception.Message, StringComparison.OrdinalIgnoreCase);
		}

		[Fact]
		public void InvalidBase64IsReportedAsAControlledSolverError() {
			var exception = Assert.Throws<CaptchaSolverException>(() =>
				OgameCaptchaSolver.GetCaptchaSolution("not-base64", "also-not-base64"));

			Assert.Contains("Base64", exception.Message, StringComparison.OrdinalIgnoreCase);
		}

		[Fact]
		public void DataUriBase64IsAcceptedByTheImageInputBoundary() {
			var exception = Assert.Throws<CaptchaSolverException>(() =>
				OgameCaptchaSolver.GetCaptchaSolution(
					$"data:image/png;base64,{Convert.ToBase64String(CreatePng(240, 8))}",
					$"data:image/png;base64,{Convert.ToBase64String(CreatePng(1, 1))}"));

			Assert.Contains("not recognized", exception.Message, StringComparison.OrdinalIgnoreCase);
		}

		[Fact]
		public void EveryQuestionMappingReferencesAUsableImageMapping() {
			var imagePairs = (Dictionary<string, string>)typeof(OgameCaptchaSolver)
				.GetField("CaptchaImagePairs", BindingFlags.NonPublic | BindingFlags.Static)
				.GetValue(null);
			var textPairs = (Dictionary<string, string>)typeof(OgameCaptchaSolver)
				.GetField("CaptchaTextPairs", BindingFlags.NonPublic | BindingFlags.Static)
				.GetValue(null);

			Assert.All(textPairs.Values, imageName => {
				Assert.True(imagePairs.TryGetValue(imageName, out var imageHash));
				Assert.False(string.IsNullOrWhiteSpace(imageHash));
			});
		}

		[Fact]
		public void CaptchaStripIsSplitIntoFourEqualTilesUsingItsActualWidth() {
			var method = typeof(OgameCaptchaSolver).GetMethod(
				"GetCaptchaImagesHashes",
				BindingFlags.NonPublic | BindingFlags.Static,
				null,
				new[] { typeof(byte[]) },
				null);

			Assert.NotNull(method);
			var hashes = (string[])method.Invoke(null, new object[] { CreatePng(240, 8) });
			Assert.Equal(4, hashes.Length);

			var invocation = Assert.Throws<TargetInvocationException>(() =>
				method.Invoke(null, new object[] { CreatePng(241, 8) }));
			Assert.IsType<InvalidDataException>(invocation.InnerException);
		}

		private static byte[] CreatePng(int width, int height) {
			using var image = new Image<Rgba32>(width, height);
			for (var y = 0; y < height; y++) {
				for (var x = 0; x < width; x++) {
					image[x, y] = new Rgba32(
						(byte)(x * 17 + y),
						(byte)(x * 7 + y),
						(byte)(255 - y),
						255);
				}
			}

			using var stream = new MemoryStream();
			image.SaveAsPng(stream);
			return stream.ToArray();
		}
	}
}
