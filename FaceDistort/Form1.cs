using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;

namespace FaceDistort
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {}

        //全局变量
        public double[,] sourceCoordinate, targetCoordinate; //坐标的原点在图片左上角
        public double[,] xyfeature; //xyfeature:12位是target区域关键点变换之后的坐标，34位是相对于source变换关键点的x，y偏移量；
        public double[] source_transfer_origin, target_transfer_origin, sourceinfo, targetinfo;
        public double[,] xphi_controlLatticeValue, yphi_controlLatticeValue, xpsi_controlLatticeValue, ypsi_controlLatticeValue, xpsi_refine_controlLatticeValue, ypsi_refine_controlLatticeValue; //控制网格格点的值；
        public double edgeLength, blockSize; //正方形人脸区域的边长；控制网格每格的大小
        public int controlLatticeSize = 5, level = 1; //控制网格一边的格点数；多层次B样条的层次数
        public Bitmap sourceImage, targetImage, distortImage; //source是原图片，target是目标脸型，distort是变形后
        public int sourceImage_h, sourceImage_w, targetImage_h, targetImage_w;
        public double[,] L, L_inv, Y, coefficient; //对应TPS求解中的符号, coefficient指代待求解的系数矩阵 = L(-1)*Y
        public int interpolationType = 0; //插值类型 1最近邻；2线性；3三次
        public int distortType = 0; //变形类型 1TPS；2Bspline
        public OpenFileDialog SourceImageRead;

        private void button1_Click(object sender, EventArgs e)
        {
            SourceImageRead = new OpenFileDialog();
            SourceImageRead.Title = "打开原图片";
            SourceImageRead.Filter = "图片文件(*.jpg)|*.jpg";
            SourceImageRead.Multiselect = false;
            SourceImageRead.RestoreDirectory = true;
            if (SourceImageRead.ShowDialog() == DialogResult.OK)
            {
                sourceImage = new Bitmap(SourceImageRead.FileName);
                sourceImage_h = sourceImage.Height;
                sourceImage_w = sourceImage.Width;
                this.pictureBox1.Image = sourceImage;
                string fileindex = System.IO.Path.GetFileNameWithoutExtension(SourceImageRead.FileName);
                string rootpath = System.IO.Path.GetDirectoryName(SourceImageRead.FileName);
                StreamReader keypoint = new StreamReader(rootpath + "/" + fileindex + ".txt");
                string line = keypoint.ReadLine();
                string[] pair;
                sourceCoordinate = new double[68, 2];
                for (int i = 0; i < 68; i++)
                {
                    try
                    {
                        pair = line.Split(' ');
                        sourceCoordinate[i, 0] = Convert.ToDouble(pair[0]);
                        sourceCoordinate[i, 1] = Convert.ToDouble(pair[1]);
                        line = keypoint.ReadLine();
                    }
                    catch (NullReferenceException)
                    {
                        Console.WriteLine("Some param is null！");
                    }
                }
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            OpenFileDialog ImageRead = new OpenFileDialog();
            ImageRead.Title = "打开目标图片";
            ImageRead.Filter = "图片文件(*.jpg)|*.jpg";
            ImageRead.Multiselect = false;
            ImageRead.RestoreDirectory = true;
            if (ImageRead.ShowDialog() == DialogResult.OK)
            {
                targetImage = new Bitmap(ImageRead.FileName);
                targetImage_h = targetImage.Height;
                targetImage_w = targetImage.Width;
                this.pictureBox2.Image = targetImage;
                string fileindex = System.IO.Path.GetFileNameWithoutExtension(ImageRead.FileName);
                string rootpath = System.IO.Path.GetDirectoryName(SourceImageRead.FileName);
                StreamReader keypoint = new StreamReader(rootpath + "/" + fileindex + ".txt");
                string line = keypoint.ReadLine();
                string[] pair;
                targetCoordinate = new double[68, 2];
                for (int i = 0; i < 68; i++)
                {
                    try
                    {
                        pair = line.Split(' ');
                        targetCoordinate[i, 0] = Convert.ToDouble(pair[0]);
                        targetCoordinate[i, 1] = Convert.ToDouble(pair[1]);
                        line = keypoint.ReadLine();
                    }
                    catch (NullReferenceException)
                    {
                        Console.WriteLine("Some param is null！");
                    }
                }
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            switch (distortType)
            {
                case 1:
                    TPS();
                    this.pictureBox4.Image = distortImage;
                    break;
                case 2:
                    Bspline();
                    this.pictureBox3.Image = distortImage;
                    break;
                default:
                    MessageBox.Show("请选择变形方式, 默认为TPS变形");
                    distortType = 1;
                    break;
            }
        }

        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {
            interpolationType = 1;
        }

        private void radioButton2_CheckedChanged(object sender, EventArgs e)
        {
            interpolationType = 2;
        }

        private void radioButton3_CheckedChanged(object sender, EventArgs e)
        {
            interpolationType = 3;
        }

        private void radioButton4_CheckedChanged(object sender, EventArgs e)//TPS
        {
            distortType = 1;
        }

        private void radioButton5_CheckedChanged(object sender, EventArgs e)
        {
            distortType = 2;
        }

        private void numericUpDown1_ValueChanged(object sender, EventArgs e)
        {
            controlLatticeSize = Convert.ToInt16(this.numericUpDown1.Value);
        }

        private void numericUpDown2_ValueChanged(object sender, EventArgs e)
        {
            level = Convert.ToInt16(this.numericUpDown2.Value);
        }

        /*-------------------------------------TPS部分-----------------------------------*/
        //径向基函数计算
        public double U(double r)
        {
            if(r == 0)
            {
                return 0;
            }
            else
            {
                double result = r * r * System.Math.Log10(r * r);
                return result;
            }
        }

        //获取线性方程组相应矩阵L(由K，P组成),Y, Y中是原图片的坐标，P中目标图片的坐标
        public void GetEquationMatrix()
        {
            L = new double[71, 71]; //L是个对称的矩阵
            Y = new double[71, 2];
            for (int i = 0; i < 71; i++)
            {
                //生成L
                double xi, yi, xj, yj;
                for (int j = 0; j < i+1; j++)
                {
                    if (i == j)
                    {
                        L[i, j] = 0;
                    }
                    else
                    {
                        switch (i)
                        {
                            case 68:
                                L[i, j] = 1;
                                L[j, i] = L[i, j];
                                break;
                            case 69:
                                if (j < 68)
                                {
                                    xi = targetCoordinate[j, 0];
                                    L[i, j] = xi;
                                    L[j, i] = L[i, j];
                                }
                                else
                                {
                                    L[i, j] = 0;
                                    L[j, i] = L[i, j];
                                }
                                break;
                            case 70:
                                if (j < 68)
                                {
                                    yi = targetCoordinate[j, 1];
                                    L[i, j] = yi;
                                    L[j, i] = L[i, j];
                                }
                                else
                                {
                                    L[i, j] = 0;
                                    L[j, i] = L[i, j];
                                }
                                break;
                            default:
                                xi = targetCoordinate[i, 0]; //目标脸型的关键点坐标
                                yi = targetCoordinate[i, 1];
                                xj = targetCoordinate[j, 0];
                                yj = targetCoordinate[j, 1];
                                double r;
                                r = System.Math.Sqrt((xi - xj)*(xi - xj) +(yi - yj)*(yi - yj)); //欧式距离
                                L[i, j] = U(r);
                                L[j, i] = L[i, j];
                                break;
                        }
                    }
                }

                //生成Y
                for (int k = 0; k < 2; k++)
                {
                    if (i >= 68)
                    {
                        Y[i, 0] = 0;
                        Y[i, 1] = 0;
                    }
                    else
                    {
                        Y[i, 0] = sourceCoordinate[i, 0];
                        Y[i, 1] = sourceCoordinate[i, 1];
                    }
                }
            }
        }

        //求解L矩阵的逆
        public double [,] inverse(double [,] M)
        {
            double[,] inv = new double[71, 71]; //inv是矩阵的逆
            inv = M; 
            double [,] unit = new double[71, 71];
            //构造一个71*71的单位矩阵
            for (int i = 0; i < 71; i++)
            {
                for (int j = 0; j < 71; j++)
                {
                    if (i == j)
                    {
                        unit[i, j] = 1;
                    }
                    else
                    {
                        unit[i, j] = 0;
                    }
                }
            }

            //i代表行号
            for (int j = 0; j < 71; j++)
            {
                for (int i = j; i < 71; i++)
                {
                    if (inv[i, j] != 0)
                    {
                        double tmp;
                        if (i != j) //如果 i>j 那么说明这行要与j行交换
                        {
                            for (int k = 0; k < 71; k++)
                            {
                                tmp = inv[j, k];
                                inv[j, k] = inv[i, k];
                                inv[i, k] = tmp;
                                
                                //同时伴随矩阵也要变化
                                tmp = unit[j, k];
                                unit[j, k] = unit[i, k];
                                unit[i, k] = tmp;
                            }
                        }

                        tmp = inv[j, j];
                        for (int k = 0; k < 71; k++) //该行对角线元素标准化成1 
                        {
                            inv[j, k] = inv[j, k] / tmp;
                            unit[j, k] = unit[j, k] / tmp;
                        }

                        for (int k = 0; k < 71; k++)
                        {
                            if (k != j)
                            {
                                double tmp1 = inv[k, j]; //第k行j列上的元素值，要把第j列全部消成0
                                for (int m = 0; m < 71; m++)
                                {
                                    inv[k, m] -= tmp1 * inv[j, m];
                                    unit[k, m] -= tmp1 * unit[j, m];
                                }
                            }
                        }
                    }
                }
            }
            inv = unit;
            return inv;
        }

        //矩阵相乘m1*m2, 1*2 * 2*3 p1=1 p2=2 p3=3
        public double[,] multiply(double [,] m1, double [,] m2 , int p1, int p2, int p3)
        {
            double[,] result = new double[p1, p3];
            for (int i = 0; i < p1; i++)
            {
                for (int j = 0; j < p3; j++)
                {
                    double tmp = 0;
                    for (int k = 0; k < p2; k++)
                    {
                        tmp += m1[i, k] * m2[k, j];
                    }
                    result[i, j] = tmp;
                }
            }
            return result;
        }

        //向量内积
        public double multiply_inner_product(double[,] m1, double[,] m2, int p)
        {
            double result = 0;
            for (int i = 0; i < p; i++)
            {
                result += m1[0, i] * m2[i, 0];
            }
            return result;
        }

        //薄板样条变形
        public void TPS()
        {
            //求解出从目标图片到原图片坐标映射的系数矩阵coefficient(71*2)，由此可以将目标图片的各个坐标映射回原图片，再插值取响应的像素值
            distortImage = new Bitmap(targetImage_w, targetImage_h);
            GetEquationMatrix(); //获取线性方程组相应矩阵L(由K，P组成),Y, Y中是原图片的坐标，P中目标图片的坐标
            coefficient = multiply(inverse(L), Y, 71, 71, 2);
            for (int xt = 0; xt < targetImage_w; xt++)
            {
                for (int yt = 0; yt < targetImage_h; yt++)
                {
                    double xs = 0, ys = 0;
                    double tmpx = 0, tmpy = 0;
                    for (int i = 0; i < 71; i++)
                    {
                        double x = xt;
                        double y = yt;
                        switch (i)
                        {
                            case 68:
                                tmpx += coefficient[i, 0];
                                tmpy += coefficient[i, 1];
                                break;
                            case 69:
                                tmpx += coefficient[i, 0] * x;
                                tmpy += coefficient[i, 1] * x;
                                break;
                            case 70:
                                tmpx += coefficient[i, 0] * y;
                                tmpy += coefficient[i, 1] * y;
                                break;
                            default:
                                double xkp = targetCoordinate[i, 0]; //xkp x-keypoint
                                double ykp = targetCoordinate[i, 1];
                                double u;
                                u = U(System.Math.Sqrt((xkp - x) * (xkp - x) + (ykp - y) * (ykp - y))); //欧式距离
                                tmpx += coefficient[i, 0] * u;
                                tmpy += coefficient[i, 1] * u;
                                break;
                        }
                    }
                    xs = tmpx;
                    ys = tmpy;
                    switch (interpolationType)
                    {
                        case 1:
                            NN(xs, ys, xt, yt);
                            break;
                        case 2:
                            Linear(xs, ys, xt, yt);
                            break;
                        case 3:
                            Cubic(xs, ys, xt, yt);
                            break;
                        default:
                            MessageBox.Show("请选择插值方式, 默认为最近邻插值");
                            interpolationType = 1;
                            break;
                    }
                }
            }
        }

        /*----------------------------------B-spline部分----------------------------------*/
        
        //提取正方形人脸区域，对坐标进行变换，对人脸鼻梁关键点进行对齐，然后获取偏移特征量xyfeature
        public void coordinate_transfer()
        {
            xyfeature = new double[68, 4]; //12位是target区域关键点变换之后的坐标，34位是相对于source变换关键点的x，y偏移量
            double[,] sourceCoordinateTransfer = new double[68, 2];
            double[,] targetCoordinateTransfer = new double[68, 2];
            sourceinfo = new double[4] { 0, sourceImage_w, 0, sourceImage_h };//xmax，xmin，ymax，ymin
            targetinfo = new double[4] { 0, targetImage_w, 0, targetImage_h };
            double tempx, tempy;

            //找到脸部区域的x，y范围，以及xmin，xmax，ymin，ymax
            for (int i = 0; i < 68; i++)
            {
                tempx = sourceCoordinate[i, 0];
                tempy = sourceCoordinate[i, 1];
                if (tempx > sourceinfo[0]) { sourceinfo[0] = tempx; }
                if (tempx < sourceinfo[1]) { sourceinfo[1] = tempx; }
                if (tempy > sourceinfo[2]) { sourceinfo[2] = tempy; }
                if (tempy < sourceinfo[3]) { sourceinfo[3] = tempy; }

                tempx = targetCoordinate[i, 0];
                tempy = targetCoordinate[i, 1];
                if (tempx > targetinfo[0]) { targetinfo[0] = tempx; }
                if (tempx < targetinfo[1]) { targetinfo[1] = tempx; }
                if (tempy > targetinfo[2]) { targetinfo[2] = tempy; }
                if (tempy < targetinfo[3]) { targetinfo[3] = tempy; }
            }

            //坐标进行变换，坐标原点从原图的原点(0,0)变为脸部区域的右上角(x,y)
            double expand_ratio = 0.51; //脸部区域每条边，在两侧扩展边长的ratio倍，注意！！！0.25时出现奇怪的条纹...
            
            if (sourceinfo[0] - sourceinfo[1] > sourceinfo[2] - sourceinfo[3])
            {
                edgeLength = (1 + 2 * expand_ratio) * (sourceinfo[0] - sourceinfo[1]);
            }
            else
            {
                edgeLength = (1 + 2 * expand_ratio) * (sourceinfo[2] - sourceinfo[3]);
            }
            
            //transfer_origin的原点是左上角
            source_transfer_origin = new double[2] { sourceinfo[1] - expand_ratio * (sourceinfo[0] - sourceinfo[1]), sourceinfo[3] - expand_ratio * (sourceinfo[2] - sourceinfo[3]) }; //x_origin_transfer, y_origin_transfer
            target_transfer_origin = new double[2] { targetinfo[1] - expand_ratio * (targetinfo[0] - targetinfo[1]), targetinfo[3] - expand_ratio * (targetinfo[2] - targetinfo[3]) };
            for (int i = 0; i < 68; i++)
            {
                //要把target的脸部区域面积变成和source区域相同大小的正方形(边长为edgeLength)，因此坐标还要进行放缩
                double xratio = edgeLength / ((1 + 2 * expand_ratio) * (targetinfo[0] - targetinfo[1]));
                double yratio = edgeLength / ((1 + 2 * expand_ratio) * (targetinfo[2] - targetinfo[3]));
                sourceCoordinateTransfer[i, 0] = sourceCoordinate[i, 0] - source_transfer_origin[0];
                sourceCoordinateTransfer[i, 1] = edgeLength - (sourceCoordinate[i, 1] - source_transfer_origin[1]); //图像坐标原点为左上角，将其坐标值转化为原点为左下角的坐标值，只用改变y值，height - y
                targetCoordinateTransfer[i, 0] = (targetCoordinate[i, 0] - target_transfer_origin[0]) * xratio;
                targetCoordinateTransfer[i, 1] = edgeLength - (targetCoordinate[i, 1] - target_transfer_origin[1]) * yratio; //图像坐标原点为左上角，将其坐标值转化为原点为左下角的坐标值，只用改变y值，height - y
            }

            double xoffset = sourceCoordinateTransfer[27, 0] - targetCoordinateTransfer[27, 0]; //27号关键点，鼻子上方第一个点，将source和target的这两点关键点对齐
            double yoffset = sourceCoordinateTransfer[27, 1] - targetCoordinateTransfer[27, 1];

            for (int i = 0; i < 68; i++)
            {
                xyfeature[i, 0] = targetCoordinateTransfer[i, 0] + xoffset; //x
                xyfeature[i, 1] = targetCoordinateTransfer[i, 1] + yoffset; //y
                xyfeature[i, 2] = sourceCoordinateTransfer[i, 0] - (targetCoordinateTransfer[i, 0] + xoffset); //dx
                xyfeature[i, 3] = sourceCoordinateTransfer[i, 1] - (targetCoordinateTransfer[i, 1] + yoffset); //dy
            }
        }

        //3次B样条基函数
        public double cubicBsplineBasisFunction(double t, int k)
        {
            double tmp = -100;
            if (t >= 0 && t < 1)
            {
                switch (k)
                {
                    case 0:
                        tmp = System.Math.Pow((1 - t), 3) / 6;
                        break;
                    case 1:
                        tmp = (3 * System.Math.Pow(t, 3) - 6 * System.Math.Pow(t, 2) + 4) / 6;
                        break;
                    case 2:
                        tmp = (-3 * System.Math.Pow(t, 3) + 3 * System.Math.Pow(t, 2) + 3 * t + 1) / 6;
                        break;
                    case 3:
                        tmp = System.Math.Pow(t, 3) / 6;
                        break;
                    default:
                        break;
                }
                return tmp;
            }
            return -100;
        }

        //wab^2 求和
        public double WabSquareSum(double s, double t)
        {
            double tmp = 0;
            for (int a = 0; a < 4; a++)
            {
                for (int b = 0; b < 4; b++)
                {
                    tmp += System.Math.Pow((cubicBsplineBasisFunction(s, a) * cubicBsplineBasisFunction(t, b)), 2);
                }
            }
            return tmp;
        }

        //BA Algorithm
        public void control_lattice_compute()
        {
            //controlLatticeSize = m + 3;
            blockSize = edgeLength / (controlLatticeSize - 3);
            xphi_controlLatticeValue = new double[controlLatticeSize, controlLatticeSize];
            yphi_controlLatticeValue = new double[controlLatticeSize, controlLatticeSize];
            double[,] xdelta = new double[controlLatticeSize, controlLatticeSize], ydelta = new double[controlLatticeSize, controlLatticeSize];
            double[,] omega = new double[controlLatticeSize, controlLatticeSize];
            for (int k = 0; k < controlLatticeSize; k++)
            {
                for (int l = 0; l < controlLatticeSize; l++)
                {
                    xdelta[k, l] = 0;
                    ydelta[k, l] = 0;
                    omega[k, l] = 0;
                }
            }

            for (int n = 0; n < 68; n++)
            {
                int i, j;
                double s, t, wab, wkl, xphi, yphi;
                i = Convert.ToInt16(System.Math.Floor((xyfeature[n, 0] / blockSize)) - 1) + 1;
                if (i >= controlLatticeSize - 3){ i = controlLatticeSize - 4; }
                if (i < 0) { i = 0; }
                j = Convert.ToInt16(System.Math.Floor((xyfeature[n, 1] / blockSize)) - 1) + 1;
                if (j >= controlLatticeSize - 3){ j = controlLatticeSize - 4; }
                if (j < 0) { j = 0; }
                s = xyfeature[n, 0] / blockSize - System.Math.Floor(xyfeature[n, 0] / blockSize);
                t = xyfeature[n, 1] / blockSize - System.Math.Floor(xyfeature[n, 1] / blockSize);
                for (int k = 0; k < 4; k++)
                {
                    for (int l = 0; l < 4; l++)
                    {
                        wkl = cubicBsplineBasisFunction(s, k) * cubicBsplineBasisFunction(t, l);
                        wab = WabSquareSum(s, t);
                        xphi = (wkl * xyfeature[n, 2]) / wab;
                        yphi = (wkl * xyfeature[n, 3]) / wab;
                        xdelta[i + k, j + l] += wkl * wkl * xphi;
                        ydelta[i + k, j + l] += wkl * wkl * yphi;
                        omega[i + k, j + l] += wkl * wkl;
                    }
                }
            }

            for (int i = 0; i < controlLatticeSize; i++)
            {
                for (int j = 0; j < controlLatticeSize; j++)
                {
                    if (omega[i, j] != 0)
                    {
                        xphi_controlLatticeValue[i, j] = xdelta[i, j] / omega[i, j];
                        yphi_controlLatticeValue[i, j] = ydelta[i, j] / omega[i, j];
                    }
                    else
                    {
                        xphi_controlLatticeValue[i, j] = 0;
                        yphi_controlLatticeValue[i, j] = 0;
                    }
                }
            }
        }

        //关键点偏移数据更新
        public void xyfeature_update()
        {
            for (int n = 0; n < 68; n++)
            {
                double x = xyfeature[n, 0];
                double y = xyfeature[n, 1];
                double dx = 0, dy = 0, s, t;
                int i, j;
                i = Convert.ToInt16(System.Math.Floor(x / blockSize) - 1) + 1;
                if (i >= controlLatticeSize - 3) { i = controlLatticeSize - 4; }
                if (i < 0) { i = 0; }
                j = Convert.ToInt16(System.Math.Floor(y / blockSize) - 1) + 1;
                if (j >= controlLatticeSize - 3) { j = controlLatticeSize - 4; }
                if (j < 0) { j = 0; }
                s = x / blockSize - System.Math.Floor(x / blockSize);
                t = y / blockSize - System.Math.Floor(y / blockSize);
                for (int k = 0; k < 4; k++)
                {
                    for (int l = 0; l < 4; l++)
                    {
                        dx += xphi_controlLatticeValue[i + k, j + l] * cubicBsplineBasisFunction(s, k) * cubicBsplineBasisFunction(t, l);
                        dy += yphi_controlLatticeValue[i + k, j + l] * cubicBsplineBasisFunction(s, k) * cubicBsplineBasisFunction(t, l);
                    }
                }
                xyfeature[n, 2] -= dx;
                xyfeature[n, 3] -= dy;
            }
        }

        //refinement
        public void refinement()
        {
            xpsi_refine_controlLatticeValue = new double[controlLatticeSize, controlLatticeSize];
            ypsi_refine_controlLatticeValue = new double[controlLatticeSize, controlLatticeSize];
            for (int i = 0; i < controlLatticeSize; i++)
            {
                for (int j = 0; j < controlLatticeSize; j++)
                {
                    xpsi_refine_controlLatticeValue[i, j] = 0;
                    ypsi_refine_controlLatticeValue[i, j] = 0;
                }
            }

            int m = ((controlLatticeSize + 3) / 2) - 3;
            for (int i = 1; i <= m + 1; i++)
            {
                for (int j = 1; j <= m + 1; j++)
                {
                    xpsi_refine_controlLatticeValue[2 * i - 1, 2 * j - 1] = (xpsi_controlLatticeValue[i - 1, j - 1] + xpsi_controlLatticeValue[i - 1, j + 1] + xpsi_controlLatticeValue[i + 1, j - 1] + xpsi_controlLatticeValue[i + 1, j + 1] + 6 * (xpsi_controlLatticeValue[i - 1, j] + xpsi_controlLatticeValue[i, j - 1] + xpsi_controlLatticeValue[i, j + 1] + xpsi_controlLatticeValue[i + 1, j]) + 36 * xpsi_controlLatticeValue[i, j]) / 64;
                    xpsi_refine_controlLatticeValue[2 * i - 1, 2 * j] = (xpsi_controlLatticeValue[i - 1, j] + xpsi_controlLatticeValue[i - 1, j + 1] + xpsi_controlLatticeValue[i + 1, j] + xpsi_controlLatticeValue[i + 1, j + 1] + 6 * (xpsi_controlLatticeValue[i, j] + xpsi_controlLatticeValue[i, j + 1])) / 16;
                    xpsi_refine_controlLatticeValue[2 * i, 2 * j - 1] = (xpsi_controlLatticeValue[i, j - 1] + xpsi_controlLatticeValue[i, j + 1] + xpsi_controlLatticeValue[i + 1, j - 1] + xpsi_controlLatticeValue[i + 1, j + 1] + 6 * (xpsi_controlLatticeValue[i, j] + xpsi_controlLatticeValue[i + 1, j])) / 16;
                    xpsi_refine_controlLatticeValue[2 * i, 2 * j] = (xpsi_controlLatticeValue[i, j] + xpsi_controlLatticeValue[i, j + 1] + xpsi_controlLatticeValue[i + 1, j] + xpsi_controlLatticeValue[i + 1, j + 1]) / 4;

                    ypsi_refine_controlLatticeValue[2 * i - 1, 2 * j - 1] = (ypsi_controlLatticeValue[i - 1, j - 1] + ypsi_controlLatticeValue[i - 1, j + 1] + ypsi_controlLatticeValue[i + 1, j - 1] + ypsi_controlLatticeValue[i + 1, j + 1] + 6 * (ypsi_controlLatticeValue[i - 1, j] + ypsi_controlLatticeValue[i, j - 1] + ypsi_controlLatticeValue[i, j + 1] + ypsi_controlLatticeValue[i + 1, j]) + 36 * ypsi_controlLatticeValue[i, j]) / 64;
                    ypsi_refine_controlLatticeValue[2 * i - 1, 2 * j] = (ypsi_controlLatticeValue[i - 1, j] + ypsi_controlLatticeValue[i - 1, j + 1] + ypsi_controlLatticeValue[i + 1, j] + ypsi_controlLatticeValue[i + 1, j + 1] + 6 * (ypsi_controlLatticeValue[i, j] + ypsi_controlLatticeValue[i, j + 1])) / 16;
                    ypsi_refine_controlLatticeValue[2 * i, 2 * j - 1] = (ypsi_controlLatticeValue[i, j - 1] + ypsi_controlLatticeValue[i, j + 1] + ypsi_controlLatticeValue[i + 1, j - 1] + ypsi_controlLatticeValue[i + 1, j + 1] + 6 * (ypsi_controlLatticeValue[i, j] + ypsi_controlLatticeValue[i + 1, j])) / 16;
                    ypsi_refine_controlLatticeValue[2 * i, 2 * j] = (ypsi_controlLatticeValue[i, j] + ypsi_controlLatticeValue[i, j + 1] + ypsi_controlLatticeValue[i + 1, j] + ypsi_controlLatticeValue[i + 1, j + 1]) / 4;
                }
            }
        }

        //B样条变形
        public void Bspline()
        {
            controlLatticeSize = Convert.ToInt16(this.numericUpDown1.Value);
            level = Convert.ToInt16(this.numericUpDown2.Value);

            distortImage = new Bitmap(SourceImageRead.FileName);
            coordinate_transfer(); //坐标变换，求偏移量xyfeature[68,4]

            //多层次B样条部分
            xpsi_refine_controlLatticeValue = new double[controlLatticeSize, controlLatticeSize];
            ypsi_refine_controlLatticeValue = new double[controlLatticeSize, controlLatticeSize];

            for (int i = 0; i < controlLatticeSize; i++)
            {
                for (int j = 0; j < controlLatticeSize; j++)
                {
                    xpsi_refine_controlLatticeValue[i, j] = 0;
                    ypsi_refine_controlLatticeValue[i, j] = 0;
                }
            }

            for (int le = 1; le <= level; le++)
            {
                control_lattice_compute(); //根据偏移量求出控制点网格的格点值 xphi_controlLatticeValue 与 yphi_controlLatticeValue
                xyfeature_update();

                //psi = psi_refine + phi
                xpsi_controlLatticeValue = new double[controlLatticeSize, controlLatticeSize];
                ypsi_controlLatticeValue = new double[controlLatticeSize, controlLatticeSize];
                for (int i = 0; i < controlLatticeSize; i++)
                {
                    for (int j = 0; j < controlLatticeSize; j++)
                    {
                        xpsi_controlLatticeValue[i, j] = xphi_controlLatticeValue[i, j] + xpsi_refine_controlLatticeValue[i, j];
                        ypsi_controlLatticeValue[i, j] = yphi_controlLatticeValue[i, j] + ypsi_refine_controlLatticeValue[i, j];
                    }
                }
                if (le != level)
                {
                    controlLatticeSize = 2 * controlLatticeSize - 3;
                    refinement(); // refine psi into psi'
                }
            }

            //变形部分
            int faceedgeLength = Convert.ToInt16(edgeLength);
            for (int x = 0; x < faceedgeLength; x++) //x，y为坐标,坐标原点为左下角，求相应点的偏移量
            {
                for (int y = 0; y < faceedgeLength; y++)
                {
                    double dx = 0, dy = 0, xs, ys, s, t;
                    int i, j;
                    i = Convert.ToInt16(System.Math.Floor(x / blockSize) - 1) + 1;
                    j = Convert.ToInt16(System.Math.Floor(y / blockSize) - 1) + 1;
                    s = x / blockSize - System.Math.Floor(x / blockSize);
                    t = y / blockSize - System.Math.Floor(y / blockSize);
                    for (int k = 0; k < 4; k++)
                    {
                        for (int l = 0; l < 4; l++)
                        {
                            dx += xpsi_controlLatticeValue[i + k, j + l] * cubicBsplineBasisFunction(s, k) * cubicBsplineBasisFunction(t, l);
                            dy += ypsi_controlLatticeValue[i + k, j + l] * cubicBsplineBasisFunction(s, k) * cubicBsplineBasisFunction(t, l);
                        }
                    }
                    xs = x + dx + source_transfer_origin[0];
                    ys = faceedgeLength - (y + dy) + source_transfer_origin[1]; //原点为左上角
                    int ytemp = faceedgeLength - y; //原点为左上角
                    int xt = Convert.ToInt16(x + source_transfer_origin[0]);
                    int yt = Convert.ToInt16(ytemp + source_transfer_origin[1]); //原点为左上角
                    switch (interpolationType)
                    {
                        case 1:
                            NN(xs, ys, xt, yt);
                            break;
                        case 2:
                            Linear(xs, ys, xt, yt);
                            break;
                        case 3:
                            Cubic(xs, ys, xt, yt);
                            break;
                        default:
                            MessageBox.Show("请选择插值方式, 默认为最近邻插值");
                            interpolationType = 1;
                            break;
                    }
                }
            }
        }

        /*----------------------------------插值部分--------------------------------------*/
        //最邻近插值
        public void NN(double xs ,double ys, int xt, int yt)
        {
            double tmpx = System.Math.Floor(xs);
            double tmpy = System.Math.Floor(ys);
            double gapx = xs - tmpx;
            double gapy = ys - tmpy;
            if (tmpx >= 0.5)
            {
                tmpx += 1.0;
                if(tmpy >= 0.5)
                {
                    tmpy += 1.0;
                }
            }
            else
            {
                if (tmpy >= 0.5)
                {
                    tmpy += 1.0;
                }
            }
            int x = Convert.ToInt16(tmpx);
            int y = Convert.ToInt16(tmpy);
            if (x >= 0 && x < sourceImage_w && y >= 0 && y < sourceImage_h)
            {
                Color pixelcolor = sourceImage.GetPixel(x, y); //从source中的对应点取RGB值 赋值给distort对应位置
                double distortImage_h = distortImage.Height;
                double distortImage_w = distortImage.Width;
                if (xt >= 0 && xt < distortImage_w && yt >= 0 && yt < distortImage_h)
                {
                    distortImage.SetPixel(xt, yt, pixelcolor);
                }
            }
            else
            {
                double distortImage_h = distortImage.Height;
                double distortImage_w = distortImage.Width;
                if (xt >= 0 && xt < distortImage_w && yt >= 0 && yt < distortImage_h)
                {
                    distortImage.SetPixel(xt, yt, Color.Black);
                }
            }
        }

        //双线性插值 (x0,y0)左下角 (x1,y0)右下角 (x0,y1)左上角 (x1,y1)右上角
        public void Linear(double xs, double ys, int xt, int yt)
        {
            int x, y; //x, y是xs, ys的整数部分
            double u, v; //u, v是xs, ys的小数部分
            x = Convert.ToInt16(System.Math.Floor(xs));
            y = Convert.ToInt16(System.Math.Floor(ys));

            if (x >= 0 && (x + 1) < sourceImage_w && y >= 0 && (y + 1) < sourceImage_h)
            {
                u = xs - x;
                v = ys - y;
                double[,] A = new double[1, 2];
                double[,] BR = new double[2, 2];
                double[,] BG = new double[2, 2];
                double[,] BB = new double[2, 2];
                double[,] C = new double[2, 1];

                A[0, 0] = 1 - u;
                A[0, 1] = u;
                C[0, 0] = 1 - v;
                C[1, 0] = v;

                for (int i = 0; i < 2; i++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        Color pixel = sourceImage.GetPixel((x + i), (y + j));
                        BR[i, j] = pixel.R;
                        BG[i, j] = pixel.G;
                        BB[i, j] = pixel.B;
                    }
                }

                double newR, newG, newB;
                newR = multiply_inner_product(multiply(A, BR, 1, 2, 2), C, 2);
                newG = multiply_inner_product(multiply(A, BG, 1, 2, 2), C, 2);
                newB = multiply_inner_product(multiply(A, BB, 1, 2, 2), C, 2);

                Color color = Color.FromArgb(Convert.ToInt16(newR), Convert.ToInt16(newG), Convert.ToInt16(newB));
                double distortImage_h = distortImage.Height;
                double distortImage_w = distortImage.Width;
                if (xt >= 0 && xt < distortImage_w && yt >= 0 && yt < distortImage_h)
                {
                    distortImage.SetPixel(xt, yt, color);
                }
            }
            else
            {
                double distortImage_h = distortImage.Height;
                double distortImage_w = distortImage.Width;
                if (xt >= 0 && xt < distortImage_w && yt >= 0 && yt < distortImage_h)
                {
                    distortImage.SetPixel(xt, yt, Color.Black);
                }
            }
        }

        //三次插值S函数
        public double S(double p)
        {
            double x = System.Math.Abs(p);
            double result;
            if (x <= 1)
            {
                result = 1 - 2 * System.Math.Pow(x,2) + System.Math.Pow(x,3);
                return result;
            }
            else 
            {
                if (x > 1 && x < 2)
                {
                    result = 4 - 8 * x + 5 * System.Math.Pow(x, 2) - System.Math.Pow(x, 3);
                    return result;
                }
                else 
                {
                    return 0;
                }
            }
        }

        //三次插值
        public void Cubic(double xs, double ys, int xt, int yt)
        {
            int x, y; //x, y是xs, ys的整数部分
            double u, v; //u, v是xs, ys的小数部分
            x = Convert.ToInt16(System.Math.Floor(xs));
            y = Convert.ToInt16(System.Math.Floor(ys));

            if ((x - 1) >= 0 && (x + 2) < sourceImage_w && (y - 1) >= 0 && (y + 2) < sourceImage_h)
            {
                u = xs - x;
                v = ys - y;
                double[,] A = new double[1, 4];
                double[,] BR = new double[4, 4];
                double[,] BG = new double[4, 4];
                double[,] BB = new double[4, 4];
                double[,] C = new double[4, 1];

                for (int i = -1; i < 3; i++)
                {
                    A[0, i + 1] = S(u - i);
                    C[i + 1, 0] = S(v - i);
                }

                for (int i = 0; i < 4; i++)
                {
                    for (int j = 0; j < 4; j++)
                    {
                        Color pixel = sourceImage.GetPixel((x + (i - 1)), (y + (j - 1)));
                        BR[i, j] = pixel.R;
                        BG[i, j] = pixel.G;
                        BB[i, j] = pixel.B;
                    }
                }

                double newR, newG, newB;
                newR = multiply_inner_product(multiply(A, BR, 1, 4, 4), C, 4); 
                newG = multiply_inner_product(multiply(A, BG, 1, 4, 4), C, 4);
                newB = multiply_inner_product(multiply(A, BB, 1, 4, 4), C, 4);

                if (newR < 0) newR = 0;
                if (newR > 255) newR = 255;
                if (newG < 0) newG = 0;
                if (newG > 255) newG = 255;
                if (newB < 0) newB = 0;
                if (newB > 255) newB = 255;
                Color color = Color.FromArgb(Convert.ToInt16(newR), Convert.ToInt16(newG), Convert.ToInt16(newB));
                double distortImage_h = distortImage.Height;
                double distortImage_w = distortImage.Width;
                if (xt >= 0 && xt < distortImage_w && yt >= 0 && yt < distortImage_h)
                {
                    distortImage.SetPixel(xt, yt, color);
                }
            }
            else
            {
                double distortImage_h = distortImage.Height;
                double distortImage_w = distortImage.Width;
                if (xt >= 0 && xt < distortImage_w && yt >= 0 && yt < distortImage_h)
                {
                    distortImage.SetPixel(xt, yt, Color.Black);
                }
            }
        }
    }
}