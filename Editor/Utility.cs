using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.Core;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

namespace ExpressionUtility
{
	internal static class Utility
	{
		private static APIUser _user;
		private static IEnumerable<ApiAvatar> _avatars;
		private static TaskCompletionSource<IEnumerable<ApiAvatar>> _avatarTSC;
		private static TaskCompletionSource<APIUser> _loginTSC;
		private static Dictionary<string, TaskCompletionSource<(string url, Texture2D image)>> _cachedTextureDownloads = new Dictionary<string, TaskCompletionSource<(string url, Texture2D image)>>();
		
		public static Task<(string url, Texture2D image)> DownloadImage(string imageUrl)
		{
			if (!_cachedTextureDownloads.TryGetValue(imageUrl, out var tcs))
			{
				tcs = new TaskCompletionSource<(string url, Texture2D image)>();
				_cachedTextureDownloads[imageUrl] = tcs;
				ImageDownloader.DownloadImage(imageUrl, 0, OnSuccess, OnFailure);
			}
			
			
			void OnSuccess(Texture2D texture2D)
			{
				var temp = new Texture2D(texture2D.width, texture2D.height, texture2D.format, texture2D.mipmapCount, false);
				Graphics.CopyTexture(texture2D, temp);
				var result = new Texture2D(texture2D.width, texture2D.height);
				
				result.SetPixels(temp.GetPixels());
				result.Apply(true);
				result.mipMapBias = -1;
				result.hideFlags = HideFlags.HideAndDontSave;
				tcs.TrySetResult((imageUrl, result));
			}	
			
			void OnFailure()
			{
				_cachedTextureDownloads.Remove(imageUrl);
				tcs.TrySetResult((imageUrl, null));
				$"Failed to download image: {imageUrl}".LogError();
			}

			return tcs.Task;
		}



		private static async Task<APIUser> Login()
		{
			if (_loginTSC != null)
			{
				return await _loginTSC.Task;
			}
			
			_loginTSC = new TaskCompletionSource<APIUser>();
	
			bool loaded = ApiCredentials.IsLoaded();
			if (!loaded)
			{
				loaded = ApiCredentials.Load();
			}

			if (!APIUser.IsLoggedIn & loaded)
			{
				API.SetOnlineMode(true);

				void Success(ApiModelContainer<APIUser> c)
				{
					_loginTSC.TrySetResult(c.Model as APIUser);
				}

				void Error(ApiModelContainer<APIUser> c)
				{
					_loginTSC.TrySetResult(c.Model as APIUser);
					_loginTSC = null;
				}
				
				APIUser.InitialFetchCurrentUser(Success, Error);
			}
			else
			{
				_loginTSC.TrySetResult(APIUser.CurrentUser);
			}


			return await _loginTSC.Task;
		}

		public static void BorderColor(this VisualElement e, Color color)
		{
			e.style.borderBottomColor = color;
			e.style.borderLeftColor = color;
			e.style.borderRightColor = color;
			e.style.borderTopColor = color;
		}
		
		public static void BorderWidth(this VisualElement e, float width)
		{
			e.style.borderBottomWidth = width;
			e.style.borderLeftWidth = width;
			e.style.borderRightWidth = width;
			e.style.borderTopWidth = width;
		}
		
		public static void BorderRadius(this VisualElement e, float radius)
		{
			e.style.borderBottomLeftRadius = radius;
			e.style.borderBottomRightRadius = radius;
			e.style.borderTopLeftRadius = radius;
			e.style.borderTopRightRadius = radius;
		}
		
		public static async Task<IEnumerable<ApiAvatar>> GetAvatars()
		{
			if (_avatarTSC != null)
			{
				return await _avatarTSC.Task;
			}

			await Login();
			_avatarTSC = new TaskCompletionSource<IEnumerable<ApiAvatar>>();
			void OnSuccess(IEnumerable<ApiAvatar> avs)
			{
				_avatarTSC.TrySetResult(avs);
			}

			ApiAvatar.FetchList(OnSuccess, s => _avatarTSC.SetResult(new List<ApiAvatar>()), 
				ApiAvatar.Owner.Mine,
				ApiAvatar.ReleaseStatus.All,
				null,
				20,
				0,
				ApiAvatar.SortHeading.None,
				ApiAvatar.SortOrder.Descending,
				null,
				null, 
				true,
				false,
				null,
				false
			);
			
			return await _avatarTSC.Task;
		}

		public static VisualElement InstantiateTemplate(this VisualTreeAsset template, VisualElement target)
		{
			template.CloneTree(target);
			return target.Children().Last();
		}
		
		public static T InstantiateTemplate<T>(this VisualTreeAsset template, VisualElement target) where T : VisualElement => InstantiateTemplate(template, target) as T;

		public static void SelectAnimatorLayer(this AnimatorController animator, AnimatorControllerLayer layer)
		{
			try
			{
				var type = Type.GetType("UnityEditor.Graphs.AnimatorControllerTool,UnityEditor.Graphs");
				EditorApplication.ExecuteMenuItem("Window/Animation/Animator");
				Selection.activeObject = animator;
				var index = Array.FindIndex(animator.layers, l => l.name == layer.name);
				var window = EditorWindow.GetWindow(type);
				var prop = type?.GetProperties().FirstOrDefault(p => p.Name == "selectedLayerIndex");
				prop?.GetSetMethod()?.Invoke(window, new object[]{index});
			}
			catch (Exception)
			{
				// ignored
			}
		}
		
		public static bool OwnsAnimator(this VRCAvatarDescriptor descriptor, RuntimeAnimatorController animator)
		{
			if (animator == null)
			{
				return false;
			}
				
			foreach (var layer in descriptor.baseAnimationLayers)
			{
				if (!layer.isDefault && layer.animatorController == animator)
				{
					return true;
				}
			}

			return false;
		}

		public static IEnumerable<AnimatorController> GetValidAnimators(this VRCAvatarDescriptor descriptor)
		{
			return descriptor.baseAnimationLayers
				.Where(b => !b.isDefault)
				.Select(b => b.animatorController)
				.Cast<AnimatorController>();
		}
		
		public static void Replace(this VisualElement root, VisualElement oldElement, VisualElement newElement)
		{
			var index = root.IndexOf(oldElement);
			root.Insert(index, newElement);
			root.Remove(oldElement);
		}

		public static IEnumerable<T> GetChildren<T>(this IAnimationDefinition instance) where T : IAnimationDefinition
		{
			foreach (var child in instance.Children)
			{
				if (child is T value)
				{
					yield return value;
				}

				foreach (var t in child.GetChildren<T>())
				{
					yield return t;
				}
			}
		}
		
		public static bool TryGetFirstChild<T>(this IAnimationDefinition instance, out T result) where T : IAnimationDefinition
		{
			result = default;
			foreach (T child in GetChildren<T>(instance))
			{
				result = child;
				return true;
			}

			return false;
		}
		
		public static bool TryGetFirstParent<T>(this IAnimationDefinition instance, out T result) where T : IAnimationDefinition
		{
			result = default;
			foreach (T parent in GetParents<T>(instance))
			{
				result = parent;
				return true;
			}

			return false;
		}
		
		public static IEnumerable<T> GetParents<T>(this IAnimationDefinition instance) where T : IAnimationDefinition
		{
			foreach (var parent in instance.Parents)
			{
				if (parent is T value)
				{
					yield return value;
				}

				foreach (var t in parent.GetParents<T>())
				{
					yield return t;
				}
			}
		}

		public static T AddChild<T>(this List<IAnimationDefinition> instance, T value) where T : IAnimationDefinition
		{
			instance?.Add(value);
			return value;
		}
		
		public static void Display(this VisualElement element, bool shouldDisplay)
		{
			if (element == null)
			{
				return;
			}
			element.style.display = shouldDisplay ? DisplayStyle.Flex : DisplayStyle.None;
		}

		public static void Log(this string msg, [CallerFilePath] string filePath = null)
		{
			var source = Path.GetFileNameWithoutExtension(filePath);
			Debug.Log($"[{source}] {msg}");
		}

		public static void LogError(this string msg, [CallerFilePath] string filePath = null)
		{
			var source = Path.GetFileNameWithoutExtension(filePath);
			Debug.LogError($"[{source}] {msg}");
		}
		
		public static void SetDirty(this IEnumerable<Object> objs)
		{
			foreach (var o in objs)
			{
				if (o == null)
				{
					continue;
				}
				EditorUtility.SetDirty(o);
			}
		}
		
		public static IEnumerable<AnimatorStateMachine> GetAnimatorStateMachinesRecursively(this AnimatorStateMachine stateMachine)
		{
			yield return stateMachine;
			var subs = stateMachine.stateMachines.SelectMany(sm => GetAnimatorStateMachinesRecursively(sm.stateMachine));
			foreach (AnimatorStateMachine sub in subs)
			{
				yield return sub;
			}
		}

		public static void RemoveObjectSelector(this ObjectField field) => field.AddToClassList("object-field--no-selector");
		public static void RemoveIcon(this ObjectField field) => field.AddToClassList("object-field--no-icon");

		public static IEnumerable<VRCExpressionsMenu> GetMenusRecursively(this VRCExpressionsMenu menu)
		{
			yield return menu;
			foreach (VRCExpressionsMenu vrcExpressionsMenu in menu.controls
				.Where(mControl => mControl.type == VRCExpressionsMenu.Control.ControlType.SubMenu && mControl.subMenu != null)
				.SelectMany(mControl => GetMenusRecursively(mControl.subMenu)))
			{
				yield return vrcExpressionsMenu;
			}
		}
		
		public static void AddObjectsToAsset(this Object asset, params Object[] objs)
		{
			var path = AssetDatabase.GetAssetPath(asset);
			if (path == "")
			{
				return;
			}
			
			foreach (var o in objs)
			{
				if (o == null)
				{
					continue;
				}

				o.hideFlags = HideFlags.HideInHierarchy;
				EditorUtility.SetDirty(o);
				AssetDatabase.AddObjectToAsset(o, path);
			}
			
			
			AssetDatabase.SaveAssets();
		}
	}
}