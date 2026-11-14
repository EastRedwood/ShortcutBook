using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace ShortcutBook;

/// <summary>아이콘을 받아 오는 동안 그 자리에 보이는 돌아가는 원호. 화면에 보일 때만 애니메이션을 돌려 CPU 를 쓰지 않는다.</summary>
public partial class Spinner : UserControl
{
    public Spinner()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue)
                Rot.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty,
                    new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
            else
                Rot.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
        };
    }
}
