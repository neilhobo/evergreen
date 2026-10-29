using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Globeweigh.Shared.Controls
{
    public class TouchButton : Button
    {
        DoubleAnimationUsingKeyFrames _animation;

        public static readonly DependencyProperty DelayElapsedProperty =
          DependencyProperty.Register("DelayElapsed", typeof(double), typeof(TouchButton), new PropertyMetadata(0d));

        public static readonly DependencyProperty DelayMillisecondsProperty =
            DependencyProperty.Register("DelayMilliseconds", typeof(int), typeof(TouchButton), new PropertyMetadata(1000));

        public double DelayElapsed
        {
            get { return (double)this.GetValue(DelayElapsedProperty); }
            set { this.SetValue(DelayElapsedProperty, value); }
        }

        public int DelayMilliseconds
        {
            get { return (int)this.GetValue(DelayMillisecondsProperty); }
            set { this.SetValue(DelayMillisecondsProperty, value); }
        }

        private void BeginDelay()
        {
            this._animation = new DoubleAnimationUsingKeyFrames() { FillBehavior = FillBehavior.Stop };
            this._animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0)), new CubicEase() { EasingMode = EasingMode.EaseIn }));
            this._animation.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(this.DelayMilliseconds)), new CubicEase() { EasingMode = EasingMode.EaseIn }));
            this._animation.Completed += (o, e) =>
            {
                this.DelayElapsed = 0d;
                this.Command.Execute(this.CommandParameter);    // Replace with whatever action you want to perform
            };

            this.BeginAnimation(DelayElapsedProperty, this._animation);
        }

        private void CancelDelay()
        {
            // Cancel animation
            this.BeginAnimation(DelayElapsedProperty, null);
        }

        private void DelayedActionCommandButton_TouchDown(object sender, System.Windows.Input.TouchEventArgs e)
        {
            this.BeginDelay();
        }

        private void DelayedActionCommandButton_TouchUp(object sender, System.Windows.Input.TouchEventArgs e)
        {
            this.CancelDelay();
        }

    }
}


